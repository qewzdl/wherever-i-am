public readonly struct ChatSendRequest
{
    public string Text { get; }
    public string ChannelId { get; }

    public ChatSendRequest(string text, string channelId = "global")
    {
        Text = text;
        ChannelId = string.IsNullOrWhiteSpace(channelId) ? "global" : channelId.Trim();
    }

    public bool IsValid(out string reason)
    {
        if (string.IsNullOrWhiteSpace(Text))
        {
            reason = "Message is empty.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public string GetNormalizedText()
    {
        return Text == null ? string.Empty : Text.Trim();
    }
}