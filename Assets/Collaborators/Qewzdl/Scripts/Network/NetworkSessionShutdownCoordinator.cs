using System;
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

    private Task shutdownTask = Task.CompletedTask;
    private Task readinessFailureTask = Task.CompletedTask;
    private ConnectionResult pendingFailure;
    private SessionScopeController sessionScopeController;
    private SceneRuntimeScopeRegistry sceneScopes;
    private SessionServiceReadinessMonitor readinessMonitor;
    private bool sessionStopRaised = true;
    private bool readinessFailureRaised;

    public bool IsShutdownInProgress => !shutdownTask.IsCompleted;
    internal IServiceResolver SessionServices => sessionScopeController != null
        ? sessionScopeController.Services
        : null;

    public event Action SessionStarted;
    public event Action SessionStopped;

    private void Awake()
    {
        HasRequiredReferences();
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
        Action<ISessionServiceRegistrar> registerServices,
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
        Action<IPlayerServiceRegistrar> registerReplicatedServices,
        Action<IPlayerServiceRegistrar> registerLocalServices,
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

        try
        {
            if (wasOpen)
                runtimeSceneScopes?.UninstallSessionScopes();

            controller.Dispose();
        }
        finally
        {
            if (wasOpen && !sessionStopRaised)
            {
                sessionStopRaised = true;
                RaiseSessionEvent(SessionStopped, nameof(SessionStopped));
            }
        }
    }

    public Task ShutdownAndWaitAsync(
        NetworkShutdownMode mode = NetworkShutdownMode.Graceful)
    {
        return RequestShutdownAsync(mode, null);
    }

    public Task ShutdownAndWaitAsync(ConnectionResult failure)
    {
        if (failure == null)
            throw new ArgumentNullException(nameof(failure));

        return RequestShutdownAsync(NetworkShutdownMode.Immediate, failure);
    }

    private Task RequestShutdownAsync(
        NetworkShutdownMode mode,
        ConnectionResult failure)
    {
        if (!HasRequiredReferences())
            return Task.CompletedTask;

        if (failure != null)
            RegisterFailure(failure);

        if (!shutdownTask.IsCompleted)
        {
            if (mode == NetworkShutdownMode.Immediate)
                connectionService.ShutdownAndWaitAsync(NetworkShutdownMode.Immediate);

            return shutdownTask;
        }

        if (!IsSessionScopeOpen() && !connectionService.IsRunning)
        {
            PresentPendingFailure();
            pendingFailure = null;
            return Task.CompletedTask;
        }

        shutdownTask = ShutdownCoreAsync(mode);
        return shutdownTask;
    }

    private async Task ShutdownCoreAsync(NetworkShutdownMode mode)
    {
        bool completed = false;

        try
        {
            EnterShutdownState();
            disconnectHandler.StopListening();
            sceneFlowService.CancelPendingOperations(ProjectOperationCancelReason.SessionShutdown);

            await ShutdownNetworkWithRecoveryAsync(mode);

            CloseSessionScopeOnce();
            LoadMainMenuAfterShutdown();
            PresentPendingFailure();
            completed = true;
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

        try
        {
            sceneScopes.UninstallSessionScopes();
            sessionScopeController.Close();
        }
        finally
        {
            if (!IsSessionScopeOpen())
            {
                sessionStopRaised = true;
                RaiseSessionEvent(SessionStopped, nameof(SessionStopped));
            }
        }
    }

    private void LoadMainMenuAfterShutdown()
    {
        if (connectionService.IsRunning)
        {
            throw new InvalidOperationException(
                "Main menu cannot be loaded before NetworkManager is fully stopped.");
        }

        if (sceneFlowService.LoadScene(ProjectSceneKind.MainMenu))
            return;

        stateMachine.ChangeState(GameState.Error);
        throw new InvalidOperationException("Failed to load main menu after network shutdown.");
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
                HandleSessionReadinessLost);
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
