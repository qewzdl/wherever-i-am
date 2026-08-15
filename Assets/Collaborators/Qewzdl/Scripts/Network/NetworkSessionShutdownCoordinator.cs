using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkSessionShutdownCoordinator : MonoBehaviour
{
    [Header("Session")]
    [SerializeField] private GameStateMachine stateMachine;
    [SerializeField] private NetworkSessionStateMachine sessionStateMachine;

    [Header("Services")]
    [SerializeField] private NetworkConnectionService connectionService;
    [SerializeField] private ProjectSceneFlowService sceneFlowService;
    [SerializeField] private NetworkSessionDisconnectHandler disconnectHandler;
    [SerializeField] private UiErrorManager errorManager;

    [Header("Shutdown Recovery")]
    [SerializeField] [Min(0)] private int shutdownTimeoutRecoveryAttempts = 1;
    [SerializeField] [Min(1f)] private float mainMenuLoadTimeoutSeconds = 15f;

    [Header("Session Readiness")]
    [SerializeField] [Min(0.05f)] private float readinessHealthCheckIntervalSeconds = 0.25f;

    private Task<NetworkShutdownResult> shutdownTask =
        Task.FromResult(NetworkShutdownResult.Success());
    private Task readinessFailureTask = Task.CompletedTask;
    private ConnectionResult pendingFailure;
    private SessionScopeController sessionScopeController;
    private SceneRuntimeScopeRegistry sceneScopes;
    private SessionServiceReadinessMonitor readinessMonitor;
    private bool sessionStopRaised = true;
    private bool readinessFailureRaised;
    private float nextReadinessHealthCheckTime;

    public bool IsShutdownInProgress => !shutdownTask.IsCompleted;
    internal bool RequiresCoordinatedShutdown =>
        IsSessionScopeOpen() || (connectionService != null && connectionService.IsRunning);
    internal IServiceResolver SessionServices => sessionScopeController != null
        ? sessionScopeController.Services
        : null;

    public event Action SessionStarted;
    public event Action SessionStopped;

    private void Awake()
    {
        HasRequiredReferences();
    }

    private void Update()
    {
        if (readinessMonitor == null ||
            Time.unscaledTime < nextReadinessHealthCheckTime)
        {
            return;
        }

        nextReadinessHealthCheckTime = Time.unscaledTime +
                                       Mathf.Max(0.05f, readinessHealthCheckIntervalSeconds);
        readinessMonitor.ValidateNow();
    }

    public bool TryOpenSessionScope()
    {
        if (!HasRequiredReferences())
            return false;

        if (IsShutdownInProgress)
        {
            Debug.LogError("Cannot open a network session while the previous session is shutting down.", this);
            return false;
        }

        if (sessionScopeController == null)
        {
            ReportSessionScopeOpenFailure(
                new InvalidOperationException("Session scope controller is not configured."));

            return false;
        }

        if (sessionScopeController.IsOpen)
        {
            Debug.LogError("Network session scope is already open.", this);
            return false;
        }

        if (!sessionScopeController.TryOpen(out Exception failure))
        {
            ReportSessionScopeOpenFailure(failure);
            return false;
        }

        if (!TryStartSessionReadinessMonitor(out failure))
        {
            try
            {
                sessionScopeController.Close();
            }
            catch (Exception cleanupFailure)
            {
                failure = failure == null
                    ? cleanupFailure
                    : new AggregateException(failure, cleanupFailure);
            }

            ReportSessionScopeOpenFailure(failure);
            return false;
        }

        pendingFailure = null;
        sessionStopRaised = false;
        readinessFailureRaised = false;
        readinessFailureTask = Task.CompletedTask;
        RaiseSessionEvent(SessionStarted, nameof(SessionStarted));
        return true;
    }

    internal bool ConfigureSessionScopeController(
        ServiceScope globalScope,
        IGameMapSessionService gameMapService,
        IGameplayNoiseService gameplayNoiseService,
        SceneRuntimeScopeRegistry runtimeSceneScopes)
    {
        if (!HasRequiredReferences())
            return false;

        if (sessionScopeController != null)
        {
            Debug.LogError("Session scope controller is already configured.", this);
            return false;
        }

        if (globalScope == null || globalScope.IsDisposed)
        {
            Debug.LogError("Cannot configure Session scope without an active Global scope.", this);
            return false;
        }

        if (runtimeSceneScopes == null)
        {
            Debug.LogError("Cannot configure Session scope without the scene scope registry.", this);
            return false;
        }

        sessionScopeController = new SessionScopeController(
            globalScope,
            gameMapService,
            gameplayNoiseService);
        sceneScopes = runtimeSceneScopes;

        return true;
    }

    internal bool TryGetSessionServiceScope(out ServiceScope scope)
    {
        scope = null;
        return sessionScopeController != null &&
               sessionScopeController.TryGetScope(out scope);
    }

    internal bool TryGetSessionServiceRegistry(out ISessionServiceRegistry registry)
    {
        registry = null;
        return sessionScopeController != null &&
               sessionScopeController.TryGetRegistry(out registry);
    }

    internal bool TryRegisterSessionServices(
        Action<IServiceRegistrar> registerServices,
        out SessionServiceRegistration registrations,
        out Exception failure)
    {
        registrations = null;

        if (sessionScopeController != null)
        {
            return sessionScopeController.TryRegisterServices(
                registerServices,
                out registrations,
                out failure);
        }

        failure = new InvalidOperationException(
            "Session scope controller is not configured.");

        return false;
    }

    internal Task ReportSessionReadinessFailureAsync(
        string source,
        string details)
    {
        if (readinessFailureRaised)
            return readinessFailureTask;

        NetworkSessionState sessionState = sessionStateMachine.CurrentState;

        if (!IsSessionScopeOpen() ||
            sessionState == NetworkSessionState.Offline ||
            sessionState == NetworkSessionState.Disconnecting ||
            sessionState == NetworkSessionState.Failed)
        {
            return Task.CompletedTask;
        }

        readinessFailureRaised = true;

        string sourceName = string.IsNullOrWhiteSpace(source)
            ? "Dynamic Session service readiness"
            : source;
        string debugMessage = string.IsNullOrWhiteSpace(details)
            ? $"{sourceName} reported a required Session service failure."
            : $"{sourceName}: {details}";

        ConnectionResult failure = ConnectionResult.Fail(
            ConnectionErrorCode.SessionServiceReadinessFailed,
            "Required network session services are unavailable.",
            debugMessage,
            true);

        RegisterFailure(failure);
        sceneFlowService.CancelPendingOperations(
            ProjectOperationCancelReason.SessionServiceReadinessLost);
        readinessFailureTask = ShutdownAfterReadinessFailureAsync();
        return readinessFailureTask;
    }

    internal bool TryOpenPlayerScope(
        ulong networkObjectId,
        ulong ownerClientId,
        bool isLocalPlayer,
        Action<IServiceRegistrar> registerReplicatedServices,
        Action<IServiceRegistrar> registerLocalServices,
        out PlayerScopeRegistration registration,
        out Exception failure)
    {
        registration = null;

        if (sessionScopeController != null)
        {
            return sessionScopeController.TryOpenPlayerScope(
                networkObjectId,
                ownerClientId,
                isLocalPlayer,
                registerReplicatedServices,
                registerLocalServices,
                out registration,
                out failure);
        }

        failure = new InvalidOperationException(
            "Session scope controller is not configured.");

        return false;
    }

    internal void DisposeSessionScopeController()
    {
        SessionScopeController controller = sessionScopeController;
        SceneRuntimeScopeRegistry runtimeSceneScopes = sceneScopes;
        sessionScopeController = null;
        sceneScopes = null;
        StopSessionReadinessMonitor();

        if (controller == null)
            return;

        bool wasOpen = controller.IsOpen;
        List<Exception> cleanupFailures = new();

        try
        {
            if (wasOpen)
            {
                try
                {
                    runtimeSceneScopes?.UninstallSessionScopes();
                }
                catch (Exception exception)
                {
                    cleanupFailures.Add(exception);
                }
            }

            try
            {
                controller.Dispose();
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }
        }
        finally
        {
            if (wasOpen && !controller.IsOpen && !sessionStopRaised)
            {
                sessionStopRaised = true;
                RaiseSessionEvent(SessionStopped, nameof(SessionStopped));
            }
        }

        for (int i = 0; i < cleanupFailures.Count; i++)
            Debug.LogException(cleanupFailures[i], this);
    }

    internal void ForceAbortForApplicationQuit()
    {
        disconnectHandler?.StopListening();
        sceneFlowService?.CancelPendingOperations(
            ProjectOperationCancelReason.SessionShutdown);
        StopSessionReadinessMonitor();
        connectionService?.ForceAbortForApplicationQuit();
        DisposeSessionScopeController();
    }

    public Task<NetworkShutdownResult> ShutdownAndWaitAsync(
        NetworkShutdownMode mode = NetworkShutdownMode.Graceful)
    {
        return RequestShutdownAsync(mode, null);
    }

    public Task<NetworkShutdownResult> ShutdownAndWaitAsync(ConnectionResult failure)
    {
        if (failure == null)
            throw new ArgumentNullException(nameof(failure));

        return RequestShutdownAsync(NetworkShutdownMode.Immediate, failure);
    }

    private Task<NetworkShutdownResult> RequestShutdownAsync(
        NetworkShutdownMode mode,
        ConnectionResult failure)
    {
        if (!HasRequiredReferences())
        {
            return Task.FromResult(NetworkShutdownResult.Failure(
                !connectionService || !connectionService.IsRunning,
                !IsSessionScopeOpen(),
                stateMachine != null && stateMachine.CurrentState == GameState.MainMenu,
                "Network shutdown coordinator is not fully configured."));
        }

        if (failure != null)
            RegisterFailure(failure);

        if (!shutdownTask.IsCompleted)
        {
            if (mode == NetworkShutdownMode.Immediate)
                connectionService.ShutdownAndWaitAsync(NetworkShutdownMode.Immediate);

            return shutdownTask;
        }

        if (!IsSessionScopeOpen() &&
            !connectionService.IsRunning &&
            stateMachine.CurrentState == GameState.MainMenu)
        {
            PresentPendingFailure();
            pendingFailure = null;
            return Task.FromResult(NetworkShutdownResult.Success());
        }

        shutdownTask = ShutdownCoreAsync(mode);
        return shutdownTask;
    }

    private async Task<NetworkShutdownResult> ShutdownCoreAsync(NetworkShutdownMode mode)
    {
        bool completed = false;

        try
        {
            EnterShutdownState();
            disconnectHandler.StopListening();
            sceneFlowService.CancelPendingOperations(ProjectOperationCancelReason.SessionShutdown);

            await ShutdownNetworkWithRecoveryAsync(mode);

            CloseSessionScopeOnce();
            await LoadMainMenuAfterShutdownAsync();
            PresentPendingFailure();
            completed = true;
            return NetworkShutdownResult.Success();
        }
        catch (Exception exception)
        {
            stateMachine.ChangeState(GameState.Error);
            Debug.LogException(exception, this);

            string message = pendingFailure != null
                ? pendingFailure.UserMessage
                : "Failed to stop the network session.";

            errorManager.ShowError(
                $"{message} Shutdown can be retried without losing cleanup order.");

            return NetworkShutdownResult.Failure(
                !connectionService.IsRunning,
                !IsSessionScopeOpen(),
                stateMachine.CurrentState == GameState.MainMenu,
                exception.Message,
                exception);
        }
        finally
        {
            if (completed)
                pendingFailure = null;
        }
    }

    private async Task ShutdownNetworkWithRecoveryAsync(NetworkShutdownMode mode)
    {
        await NetworkShutdownRecoveryPolicy.ExecuteAsync(
            connectionService.ShutdownAndWaitAsync,
            mode,
            Mathf.Max(0, shutdownTimeoutRecoveryAttempts),
            (nextAttempt, attemptCount) =>
            {
                Debug.LogWarning(
                    $"Network shutdown timed out. Retrying immediately " +
                    $"({nextAttempt}/{attemptCount}) while keeping Session " +
                    "scopes alive.",
                    this);
            });
    }

    private void RegisterFailure(ConnectionResult failure)
    {
        if (pendingFailure == null)
            pendingFailure = failure;

        Debug.LogWarning(failure.DebugMessage, this);

        NetworkSessionState currentState = sessionStateMachine.CurrentState;

        if (currentState != NetworkSessionState.Failed &&
            currentState != NetworkSessionState.Offline)
        {
            sessionStateMachine.TryChangeState(NetworkSessionState.Failed, failure.DebugMessage);
        }

        stateMachine.ChangeState(GameState.Error);
    }

    private void EnterShutdownState()
    {
        if (pendingFailure != null)
            return;

        NetworkSessionState currentState = sessionStateMachine.CurrentState;

        if (currentState != NetworkSessionState.Disconnecting &&
            currentState != NetworkSessionState.Offline)
        {
            sessionStateMachine.TryChangeState(
                NetworkSessionState.Disconnecting,
                "Shutdown to main menu requested.");
        }

        stateMachine.ChangeState(GameState.Disconnecting);
    }

    private void CloseSessionScopeOnce()
    {
        if (sessionStateMachine.CurrentState != NetworkSessionState.Offline)
        {
            sessionStateMachine.TryChangeState(
                NetworkSessionState.Offline,
                "NetworkManager stopped.");
        }

        if (!IsSessionScopeOpen() || sessionStopRaised)
            return;

        StopSessionReadinessMonitor();
        List<Exception> cleanupFailures = new();

        try
        {
            try
            {
                sceneScopes.UninstallSessionScopes();
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }

            try
            {
                sessionScopeController.Close();
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }
        }
        finally
        {
            if (!IsSessionScopeOpen())
            {
                sessionStopRaised = true;
                RaiseSessionEvent(SessionStopped, nameof(SessionStopped));
            }
        }

        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException(
                "Network stopped, but one or more Session scopes failed to clean up.",
                cleanupFailures);
        }
    }

    private async Task LoadMainMenuAfterShutdownAsync()
    {
        if (connectionService.IsRunning)
        {
            throw new InvalidOperationException(
                "Main menu cannot be loaded before NetworkManager is fully stopped.");
        }

        TaskCompletionSource<bool> completion = new();

        void HandleCompleted(ProjectSceneKind sceneKind)
        {
            if (sceneKind == ProjectSceneKind.MainMenu)
                completion.TrySetResult(true);
        }

        void HandleFailed(ProjectSceneKind sceneKind)
        {
            if (sceneKind == ProjectSceneKind.MainMenu)
            {
                completion.TrySetException(
                    new InvalidOperationException(
                        "Main menu scene operation failed after network shutdown."));
            }
        }

        sceneFlowService.SceneLoadCompleted += HandleCompleted;
        sceneFlowService.SceneLoadFailed += HandleFailed;

        try
        {
            if (!sceneFlowService.LoadScene(ProjectSceneKind.MainMenu))
            {
                throw new InvalidOperationException(
                    "Failed to start main menu loading after network shutdown.");
            }

            Task finished = await Task.WhenAny(
                completion.Task,
                Task.Delay(TimeSpan.FromSeconds(
                    Mathf.Max(1f, mainMenuLoadTimeoutSeconds))));

            if (finished != completion.Task)
            {
                throw new TimeoutException(
                    "Timed out while waiting for the MainMenu scene commit.");
            }

            await completion.Task;

            if (stateMachine.CurrentState != GameState.MainMenu)
            {
                throw new InvalidOperationException(
                    "Main menu scene completed without committing MainMenu state.");
            }
        }
        finally
        {
            sceneFlowService.SceneLoadCompleted -= HandleCompleted;
            sceneFlowService.SceneLoadFailed -= HandleFailed;
        }
    }

    private void PresentPendingFailure()
    {
        if (pendingFailure != null)
            errorManager.ShowError(pendingFailure.UserMessage);
    }

    private bool IsSessionScopeOpen()
    {
        return sessionScopeController != null && sessionScopeController.IsOpen;
    }

    private void ReportSessionScopeOpenFailure(Exception failure)
    {
        Debug.LogError("Failed to open Session service scope.", this);

        if (failure != null)
            Debug.LogException(failure, this);

        errorManager.ShowError("Failed to start the network session.");
    }

    private bool TryStartSessionReadinessMonitor(out Exception failure)
    {
        StopSessionReadinessMonitor();

        if (sessionScopeController == null ||
            !sessionScopeController.TryGetRegistry(
                out ISessionServiceRegistry registry))
        {
            failure = new InvalidOperationException(
                "Cannot monitor dynamic Session services without an active registry.");
            return false;
        }

        try
        {
            readinessMonitor = new SessionServiceReadinessMonitor(
                registry,
                stateMachine,
                HandleSessionReadinessLost,
                () => connectionService != null && connectionService.IsServer);
            nextReadinessHealthCheckTime = Time.unscaledTime;
            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            failure = exception;
            return false;
        }
    }

    private void StopSessionReadinessMonitor()
    {
        SessionServiceReadinessMonitor monitor = readinessMonitor;
        readinessMonitor = null;
        nextReadinessHealthCheckTime = 0f;
        monitor?.Dispose();
    }

    private void HandleSessionReadinessLost(string error)
    {
        _ = ReportSessionReadinessFailureAsync(
            nameof(SessionServiceReadinessPolicy),
            error);
    }

    private async Task ShutdownAfterReadinessFailureAsync()
    {
        await Task.Yield();

        NetworkSessionState sessionState = sessionStateMachine.CurrentState;

        if (!IsSessionScopeOpen() ||
            sessionState == NetworkSessionState.Offline ||
            sessionState == NetworkSessionState.Disconnecting)
        {
            return;
        }

        await RequestShutdownAsync(NetworkShutdownMode.Immediate, null);
    }

    private void RaiseSessionEvent(Action handlers, string eventName)
    {
        RuntimeEventDispatcher.Invoke(handlers, eventName, this);
    }

    private bool HasRequiredReferences()
    {
        bool valid = true;

        valid &= ValidateRequiredReference(stateMachine, nameof(stateMachine));
        valid &= ValidateRequiredReference(sessionStateMachine, nameof(sessionStateMachine));
        valid &= ValidateRequiredReference(connectionService, nameof(connectionService));
        valid &= ValidateRequiredReference(sceneFlowService, nameof(sceneFlowService));
        valid &= ValidateRequiredReference(disconnectHandler, nameof(disconnectHandler));
        valid &= ValidateRequiredReference(errorManager, nameof(errorManager));

        return valid;
    }

    private bool ValidateRequiredReference(UnityEngine.Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError($"{nameof(NetworkSessionShutdownCoordinator)} is missing '{fieldName}'.", this);
        return false;
    }
}
