using UnityEngine;

public class ChatVisibilityController : MonoBehaviour
{
    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    [Header("Initial State")]
    [SerializeField] private ChatVisibilityState initialState = ChatVisibilityState.Closed;

    public ChatVisibilityState CurrentState { get; private set; }
    public bool IsOpen => CurrentState == ChatVisibilityState.Open;

    private bool hasStarted;

    public void SetEventChannel(ChatEventChannel chatEvents)
    {
        if (this.chatEvents == chatEvents)
        {
            return;
        }

        this.chatEvents = chatEvents;

        if (hasStarted && isActiveAndEnabled)
        {
            RaiseCurrentState();
        }
    }

    private void Awake()
    {
        CurrentState = initialState;
    }

    private void Start()
    {
        hasStarted = true;
        RaiseCurrentState();
    }

    public void OpenChat()
    {
        SetState(ChatVisibilityState.Open);
    }

    public void CloseChat()
    {
        SetState(ChatVisibilityState.Closed);
    }

    public void ToggleChat()
    {
        SetState(IsOpen ? ChatVisibilityState.Closed : ChatVisibilityState.Open);
    }

    public void SetState(ChatVisibilityState newState)
    {
        if (CurrentState == newState)
        {
            return;
        }

        ChatVisibilityState previousState = CurrentState;
        CurrentState = newState;

        if (!HasEventChannel())
        {
            return;
        }

        chatEvents.RaiseVisibilityChanged(new ChatVisibilityChangedEvent(previousState, CurrentState));
    }

    private void RaiseCurrentState()
    {
        if (!HasEventChannel())
        {
            return;
        }

        chatEvents.RaiseVisibilityChanged(new ChatVisibilityChangedEvent(CurrentState, CurrentState));
    }

    private bool HasEventChannel()
    {
        return chatEvents != null;
    }
}
