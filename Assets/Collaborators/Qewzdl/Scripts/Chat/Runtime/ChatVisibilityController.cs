using UnityEngine;

public class ChatVisibilityController : MonoBehaviour
{
    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    [Header("Initial State")]
    [SerializeField] private ChatVisibilityState initialState = ChatVisibilityState.Closed;

    public ChatVisibilityState CurrentState { get; private set; }
    public bool IsOpen => CurrentState == ChatVisibilityState.Open;

    private void Awake()
    {
        ResolveEventChannel();
        CurrentState = initialState;
    }

    private void Start()
    {
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

        ResolveEventChannel();
        chatEvents.RaiseVisibilityChanged(new ChatVisibilityChangedEvent(previousState, CurrentState));
    }

    private void RaiseCurrentState()
    {
        ResolveEventChannel();
        chatEvents.RaiseVisibilityChanged(new ChatVisibilityChangedEvent(CurrentState, CurrentState));
    }

    private void ResolveEventChannel()
    {
        chatEvents = ChatEventChannel.Resolve(chatEvents);
    }
}
