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

    private Task shutdownTask = Task.CompletedTask;
    private ConnectionResult pendingFailure;
    private SessionScopeController sessionScopeController;
    private bool sessionStopRaised = true;

    public bool IsShutdownInProgress => !shutdownTask.IsCompleted;
    public IServiceResolver SessionServices => sessionScopeController != null
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

        pendingFailure = null;
        sessionStopRaised = false;
        RaiseSessionEvent(SessionStarted, nameof(SessionStarted));
        return true;
    }

    internal bool ConfigureSessionScopeController(
        ServiceScope globalScope,
        IGameMapSessionService gameMapService,
        IGameplayNoiseService gameplayNoiseService)
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

        sessionScopeController = new SessionScopeController(
            globalScope,
            gameMapService,
            gameplayNoiseService);

        return true;
    }

    internal void DisposeSessionScopeController()
    {
        SessionScopeController controller = sessionScopeController;
        sessionScopeController = null;

        if (controller == null)
            return;

        bool wasOpen = controller.IsOpen;

        try
        {
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
        try
        {
            EnterShutdownState();
            disconnectHandler.StopListening();
            sceneFlowService.CancelPendingOperations(ProjectOperationCancelReason.SessionShutdown);

            await connectionService.ShutdownAndWaitAsync(mode);

            CloseSessionScopeOnce();
            LoadMainMenuAfterShutdown();
            PresentPendingFailure();
        }
        catch (Exception exception)
        {
            stateMachine.ChangeState(GameState.Error);
            Debug.LogException(exception, this);

            string message = pendingFailure != null
                ? pendingFailure.UserMessage
                : "Failed to stop the network session.";

            errorManager.ShowError(message);
        }
        finally
        {
            pendingFailure = null;
        }
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

        try
        {
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

    private void RaiseSessionEvent(Action handlers, string eventName)
    {
        if (handlers == null)
            return;

        Delegate[] subscribers = handlers.GetInvocationList();

        for (int i = 0; i < subscribers.Length; i++)
        {
            try
            {
                ((Action)subscribers[i]).Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogError($"Subscriber failed while handling {eventName}.", this);
                Debug.LogException(exception, this);
            }
        }
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
