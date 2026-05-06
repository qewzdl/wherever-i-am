public readonly struct ChatSendRejectedEvent
{
    public ChatSendRequest Request { get; }
    public string Reason { get; }

    public ChatSendRejectedEvent(ChatSendRequest request, string reason)
    {
        Request = request;
        Reason = string.IsNullOrWhiteSpace(reason) ? "Message was rejected." : reason;
    }
}