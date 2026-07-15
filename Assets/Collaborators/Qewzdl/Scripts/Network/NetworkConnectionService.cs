using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.Serialization;

public class NetworkConnectionService : MonoBehaviour, INetworkConnectionService
{
    [Header("Network References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private UnityTransport transport;

    [Header("Configuration")]
    [SerializeField] private NetworkConnectionConfig connectionConfig;
    [FormerlySerializedAs("shutdownWarningTimeoutSeconds")]
    [SerializeField, Min(1f)] private float shutdownTimeoutSeconds = 15f;

    private readonly Dictionary<ConnectionMode, IConnectionStrategy> strategies = new Dictionary<ConnectionMode, IConnectionStrategy>();

    private CancellationTokenSource connectionAttemptCancellation;
    private Task connectionAttemptTask = Task.CompletedTask;
    private Task shutdownTask = Task.CompletedTask;
    private bool immediateShutdownRequested;

    public bool IsHost => networkManager != null && networkManager.IsHost;
    public bool IsClient => networkManager != null && networkManager.IsClient;
    public bool IsServer => networkManager != null && networkManager.IsServer;
    public bool IsConnected => networkManager != null && networkManager.IsConnectedClient;
    public bool IsListening => networkManager != null && networkManager.IsListening;
    public bool IsRunning => networkManager != null && !IsFullyStopped(networkManager);
    public bool IsConnectionReady => networkManager != null &&
                                     !networkManager.ShutdownInProgress &&
                                     IsListening &&
                                     (IsHost || IsServer || (IsClient && IsConnected));

    private void Awake()
    {
        if (!ValidateRequiredReferences())
        {
            enabled = false;
            return;
        }

        ApplyProtocolVersion();
        InitializeStrategies();
    }

    public Task<ConnectionResult> StartHostAsync()
    {
        if (!TryCreateHostConnectionConfig(out ConnectionConfig config, out ConnectionResult error))
            return Task.FromResult(error);

        return StartConnectionAsync(config);
    }

    public Task<ConnectionResult> StartClientAsync(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return Task.FromResult(ConnectionResult.Fail(
                ConnectionErrorCode.EmptyIpAddress,
                "Failed to start the network connection.",
                "Client IP address is empty.",
                true
            ));
        }

        if (!TryCreateClientConnectionConfig(ip, out ConnectionConfig config, out ConnectionResult error))
            return Task.FromResult(error);

        return StartConnectionAsync(config);
    }

    public async Task<ConnectionResult> StartConnectionAsync(ConnectionConfig config)
    {
        ConnectionResult validationResult = CanStartConnection();

        if (!validationResult.Success)
        {
            Debug.LogError(validationResult.DebugMessage);
            return validationResult;
        }

        ApplyProtocolVersion();

        if (!strategies.TryGetValue(config.Mode, out IConnectionStrategy strategy))
        {
            ConnectionResult result = ConnectionResult.Fail(
                ConnectionErrorCode.StrategyNotFound,
                "Failed to start the connection.",
                $"Connection strategy not found for mode: {config.Mode}.",
                false
            );

            Debug.LogError(result.DebugMessage);
            return result;
        }

        CancellationTokenSource attemptCancellation = new CancellationTokenSource();
        TaskCompletionSource<bool> attemptFinishedSource = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        connectionAttemptCancellation = attemptCancellation;
        connectionAttemptTask = attemptFinishedSource.Task;

        ConnectionResult connectionResult;
        bool attemptCancelled;

        try
        {
            switch (config.Role)
            {
                case ConnectionRole.Host:
                    connectionResult = await strategy.StartHostAsync(config, attemptCancellation.Token);
                    break;

                case ConnectionRole.Client:
                    connectionResult = await strategy.StartClientAsync(config, attemptCancellation.Token);
                    break;

                case ConnectionRole.Server:
                    connectionResult = await strategy.StartServerAsync(config, attemptCancellation.Token);
                    break;

                default:
                    connectionResult = ConnectionResult.Fail(
                        ConnectionErrorCode.UnsupportedConnectionRole,
                        "Failed to start the connection.",
                        $"Unsupported connection role: {config.Role}.",
                        false
                    );
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            connectionResult = CreateCancelledResult();
        }
        finally
        {
            attemptCancelled = attemptCancellation.IsCancellationRequested;

            if (connectionAttemptCancellation == attemptCancellation)
                connectionAttemptCancellation = null;

            attemptCancellation.Dispose();
            attemptFinishedSource.TrySetResult(true);

            if (connectionAttemptTask == attemptFinishedSource.Task)
                connectionAttemptTask = Task.CompletedTask;
        }

        if (attemptCancelled)
            return CreateCancelledResult();

        if (connectionResult.Success)
        {
            RuntimeLog.Info(connectionResult.DebugMessage);
        }
        else
        {
            Debug.LogError(connectionResult.DebugMessage);

            if (IsRunning)
            {
                try
                {
                    await ShutdownAndWaitAsync(NetworkShutdownMode.Immediate);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
        }

        return connectionResult;
    }

    public Task ShutdownAndWaitAsync(
        NetworkShutdownMode mode = NetworkShutdownMode.Graceful)
    {
        CancelPendingConnectionAttempt();

        if (networkManager == null)
        {
            return Task.FromException(
                new InvalidOperationException(
                    $"{nameof(NetworkConnectionService)} is missing {nameof(NetworkManager)}."));
        }

        if (!shutdownTask.IsCompleted)
        {
            if (mode == NetworkShutdownMode.Immediate && !immediateShutdownRequested)
            {
                immediateShutdownRequested = true;
                networkManager.Shutdown(discardMessageQueue: true);
            }

            return shutdownTask;
        }

        Task pendingConnectionAttempt = connectionAttemptTask;

        if (IsFullyStopped(networkManager) && pendingConnectionAttempt.IsCompleted)
            return Task.CompletedTask;

        immediateShutdownRequested = mode == NetworkShutdownMode.Immediate;
        shutdownTask = ShutdownCoreAsync(networkManager, pendingConnectionAttempt);
        return shutdownTask;
    }

    private async Task ShutdownCoreAsync(
        NetworkManager manager,
        Task pendingConnectionAttempt)
    {
        bool clientStopPending = manager.IsClient;
        bool serverStopPending = manager.IsServer;

        if (!clientStopPending && !serverStopPending)
        {
            if (!IsFullyStopped(manager))
                manager.Shutdown(immediateShutdownRequested);

            await WaitUntilFullyStoppedAsync(manager, null, null);
            await pendingConnectionAttempt;
            RuntimeLog.Info("Network shutdown.");
            return;
        }

        TaskCompletionSource<bool> stoppedSource = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void TryComplete()
        {
            if (!clientStopPending && !serverStopPending)
                stoppedSource.TrySetResult(true);
        }

        void HandleClientStopped(bool wasHost)
        {
            clientStopPending = false;
            TryComplete();
        }

        void HandleServerStopped(bool wasHost)
        {
            serverStopPending = false;
            TryComplete();
        }

        manager.OnClientStopped += HandleClientStopped;
        manager.OnServerStopped += HandleServerStopped;

        try
        {
            if (!IsFullyStopped(manager))
                manager.Shutdown(immediateShutdownRequested);

            TryComplete();
            await WaitUntilFullyStoppedAsync(
                manager,
                stoppedSource.Task,
                () => $"Client stop pending: {clientStopPending}. " +
                      $"Server stop pending: {serverStopPending}.");
            await stoppedSource.Task;
        }
        finally
        {
            if (manager != null)
            {
                manager.OnClientStopped -= HandleClientStopped;
                manager.OnServerStopped -= HandleServerStopped;
            }
        }

        await pendingConnectionAttempt;
        RuntimeLog.Info("Network shutdown.");
    }

    private async Task WaitUntilFullyStoppedAsync(
        NetworkManager manager,
        Task requiredCallbacks,
        Func<string> callbackState)
    {
        DateTime timeoutAt = DateTime.UtcNow.AddSeconds(shutdownTimeoutSeconds);

        while (!IsFullyStopped(manager) ||
               (requiredCallbacks != null && !requiredCallbacks.IsCompleted))
        {
            if (DateTime.UtcNow >= timeoutAt)
            {
                string callbackDetails = callbackState != null
                    ? $" {callbackState.Invoke()}"
                    : string.Empty;

                throw new TimeoutException(
                    $"Network shutdown did not complete within {shutdownTimeoutSeconds} seconds. " +
                    $"IsListening: {manager != null && manager.IsListening}. " +
                    $"IsClient: {manager != null && manager.IsClient}. " +
                    $"IsServer: {manager != null && manager.IsServer}. " +
                    $"ShutdownInProgress: {manager != null && manager.ShutdownInProgress}." +
                    callbackDetails);
            }

            await Task.Yield();
        }
    }

    private void CancelPendingConnectionAttempt()
    {
        if (connectionAttemptCancellation == null)
            return;

        connectionAttemptCancellation.Cancel();
    }

    private static ConnectionResult CreateCancelledResult()
    {
        return ConnectionResult.Fail(
            ConnectionErrorCode.Cancelled,
            "Connection attempt was cancelled.",
            "Network connection attempt was cancelled because the session is shutting down.",
            true);
    }

    private static bool IsFullyStopped(NetworkManager manager)
    {
        return manager == null ||
               (!manager.IsListening &&
                !manager.IsClient &&
                !manager.IsServer &&
                !manager.ShutdownInProgress);
    }

    private bool TryCreateHostConnectionConfig(out ConnectionConfig config, out ConnectionResult error)
    {
        config = null;
        error = null;

        if (!ValidateConnectionConfig())
        {
            error = ConnectionResult.Fail(
                ConnectionErrorCode.Unknown,
                "Failed to start the network connection.",
                "Network connection config is missing or invalid.",
                false
            );

            return false;
        }

        config = new ConnectionConfig(
            ConnectionMode.Lan,
            ConnectionRole.Host,
            connectionConfig.HostAddress,
            connectionConfig.Port,
            connectionConfig.ListenAddress,
            connectionConfig.ClientConnectionTimeoutSeconds
        );

        return true;
    }

    private bool TryCreateClientConnectionConfig(string ip, out ConnectionConfig config, out ConnectionResult error)
    {
        config = null;
        error = null;

        if (!ValidateConnectionConfig())
        {
            error = ConnectionResult.Fail(
                ConnectionErrorCode.Unknown,
                "Failed to start the network connection.",
                "Network connection config is missing or invalid.",
                false
            );

            return false;
        }

        config = new ConnectionConfig(
            ConnectionMode.Lan,
            ConnectionRole.Client,
            ip,
            connectionConfig.Port,
            connectionConfig.ListenAddress,
            connectionConfig.ClientConnectionTimeoutSeconds
        );

        return true;
    }

    private bool ValidateRequiredReferences()
    {
        bool isValid = true;

        if (networkManager == null)
        {
            Debug.LogError("NetworkConnectionService requires NetworkManager to be assigned explicitly in the Inspector.", this);
            isValid = false;
        }

        if (transport == null)
        {
            Debug.LogError("NetworkConnectionService requires UnityTransport to be assigned explicitly in the Inspector.", this);
            isValid = false;
        }

        if (!ValidateConnectionConfig())
            isValid = false;

        return isValid;
    }

    private bool ValidateConnectionConfig()
    {
        if (connectionConfig == null)
        {
            Debug.LogError($"{nameof(NetworkConnectionService)} is missing {nameof(NetworkConnectionConfig)}.", this);
            return false;
        }

        return connectionConfig.Validate(this);
    }

    private void InitializeStrategies()
    {
        strategies.Clear();

        IConnectionStrategy lanStrategy = new LanConnectionStrategy(networkManager, transport);

        strategies.Add(lanStrategy.Mode, lanStrategy);
    }

    private void ApplyProtocolVersion()
    {
        networkManager.NetworkConfig.ProtocolVersion = connectionConfig.ProtocolVersion;
    }

    private ConnectionResult CanStartConnection()
    {
        if (networkManager == null)
        {
            return ConnectionResult.Fail(
                ConnectionErrorCode.NetworkManagerMissing,
                "Failed to start the network connection.",
                "NetworkConnectionService requires NetworkManager to be assigned explicitly in the Inspector.",
                false
            );
        }

        if (transport == null)
        {
            return ConnectionResult.Fail(
                ConnectionErrorCode.TransportMissing,
                "Failed to start the network connection.",
                "NetworkConnectionService requires UnityTransport to be assigned explicitly in the Inspector.",
                false
            );
        }

        if (!ValidateConnectionConfig())
        {
            return ConnectionResult.Fail(
                ConnectionErrorCode.Unknown,
                "Failed to start the network connection.",
                "Network connection config is missing or invalid.",
                false
            );
        }

        if (strategies.Count == 0)
        {
            return ConnectionResult.Fail(
                ConnectionErrorCode.StrategyNotFound,
                "Failed to start the network connection.",
                "NetworkConnectionService connection strategies are not initialized.",
                false
            );
        }

        if (!IsFullyStopped(networkManager))
        {
            return ConnectionResult.Fail(
                ConnectionErrorCode.NetworkAlreadyRunning,
                "The network session is already running.",
                "Network is already running.",
                false
            );
        }

        if (connectionAttemptCancellation != null)
        {
            return ConnectionResult.Fail(
                ConnectionErrorCode.NetworkAlreadyRunning,
                "A network connection attempt is already in progress.",
                "Network connection attempt is already in progress.",
                true
            );
        }

        if (!shutdownTask.IsCompleted)
        {
            return ConnectionResult.Fail(
                ConnectionErrorCode.NetworkAlreadyRunning,
                "The previous network session is still shutting down.",
                "Cannot start a connection while network shutdown is in progress.",
                true
            );
        }

        return ConnectionResult.Ok("Connection can be started.");
    }
}
