public readonly struct ChatVisibilityChangedEvent
{
    public ChatVisibilityState PreviousState { get; }
    public ChatVisibilityState CurrentState { get; }

    public bool IsOpen => CurrentState == ChatVisibilityState.Open;

    public ChatVisibilityChangedEvent(ChatVisibilityState previousState, ChatVisibilityState currentState)
    {
        PreviousState = previousState;
        CurrentState = currentState;
    }
}