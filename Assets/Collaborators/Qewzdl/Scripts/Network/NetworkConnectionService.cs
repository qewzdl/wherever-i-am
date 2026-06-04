using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkConnectionService : MonoBehaviour
{
    [Header("Network References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private UnityTransport transport;

    [Header("Configuration")]
    [SerializeField] private NetworkConnectionConfig connectionConfig;

    private readonly Dictionary<ConnectionMode, IConnectionStrategy> strategies = new Dictionary<ConnectionMode, IConnectionStrategy>();

    private IConnectionStrategy activeStrategy;

    public bool IsHost => networkManager != null && networkManager.IsHost;
    public bool IsClient => networkManager != null && networkManager.IsClient;
    public bool IsServer => networkManager != null && networkManager.IsServer;
    public bool IsConnected => networkManager != null && networkManager.IsConnectedClient;
    public bool IsListening => networkManager != null && networkManager.IsListening;

    private void Awake()
    {
        if (!ValidateRequiredReferences())
        {
            enabled = false;
            return;
        }

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

        ConnectionResult connectionResult;

        switch (config.Role)
        {
            case ConnectionRole.Host:
                connectionResult = await strategy.StartHostAsync(config);
                break;

            case ConnectionRole.Client:
                connectionResult = await strategy.StartClientAsync(config);
                break;

            case ConnectionRole.Server:
                connectionResult = await strategy.StartServerAsync(config);
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

        if (connectionResult.Success)
        {
            activeStrategy = strategy;
            RuntimeLog.Info(connectionResult.DebugMessage);
        }
        else
        {
            Debug.LogError(connectionResult.DebugMessage);
        }

        return connectionResult;
    }

    public void Shutdown()
    {
        if (activeStrategy != null)
        {
            activeStrategy.Shutdown();
            activeStrategy = null;

            RuntimeLog.Info("Network shutdown by active strategy.");
            return;
        }

        if (networkManager == null)
            return;

        if (!networkManager.IsListening &&
            !networkManager.IsClient &&
            !networkManager.IsServer)
        {
            return;
        }

        networkManager.Shutdown();

        RuntimeLog.Info("Network shutdown.");
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

        if (networkManager.IsListening)
        {
            return ConnectionResult.Fail(
                ConnectionErrorCode.NetworkAlreadyRunning,
                "The network session is already running.",
                "Network is already running.",
                false
            );
        }

        return ConnectionResult.Ok("Connection can be started.");
    }
}
