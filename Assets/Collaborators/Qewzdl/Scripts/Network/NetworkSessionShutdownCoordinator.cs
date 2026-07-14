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
    private bool sessionScopeOpen;
    private bool sessionStopRaised = true;

    public bool IsShutdownInProgress => !shutdownTask.IsCompleted;

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

        if (sessionScopeOpen)
        {
            Debug.LogError("Network session scope is already open.", this);
            return false;
        }

        pendingFailure = null;
        sessionScopeOpen = true;
        sessionStopRaised = false;
        RaiseSessionEvent(SessionStarted, nameof(SessionStarted));
        return true;
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

        if (!sessionScopeOpen && !connectionService.IsRunning)
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

        if (!sessionScopeOpen || sessionStopRaised)
            return;

        sessionScopeOpen = false;
        sessionStopRaised = true;
        RaiseSessionEvent(SessionStopped, nameof(SessionStopped));
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
