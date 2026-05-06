public readonly struct ChatMessageReceivedEvent
{
    public string MessageId { get; }
    public string ChannelId { get; }
    public ulong SenderClientId { get; }
    public string SenderDisplayName { get; }
    public string Text { get; }
    public bool IsLocalSender { get; }
    public bool IsSystemMessage { get; }
    public double ServerTime { get; }

    public ChatMessageReceivedEvent(
        string messageId,
        string channelId,
        ulong senderClientId,
        string senderDisplayName,
        string text,
        bool isLocalSender,
        bool isSystemMessage,
        double serverTime)
    {
        MessageId = string.IsNullOrWhiteSpace(messageId) ? "unknown" : messageId;
        ChannelId = string.IsNullOrWhiteSpace(channelId) ? "global" : channelId;
        SenderClientId = senderClientId;
        SenderDisplayName = string.IsNullOrWhiteSpace(senderDisplayName) ? $"Player {senderClientId}" : senderDisplayName;
        Text = text ?? string.Empty;
        IsLocalSender = isLocalSender;
        IsSystemMessage = isSystemMessage;
        ServerTime = serverTime;
    }
}