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
        CurrentState = initialState;

        if (chatEvents == null)
        {
            Debug.LogError($"{nameof(ChatVisibilityController)} requires an assigned {nameof(ChatEventChannel)}.", this);
        }
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
        if (chatEvents != null)
        {
            return true;
        }

        Debug.LogError($"{nameof(ChatVisibilityController)} requires an assigned {nameof(ChatEventChannel)}.", this);
        return false;
    }
}
