public readonly struct ChatUnreadCountChangedEvent
{
    public int PreviousUnreadCount { get; }
    public int UnreadCount { get; }

    public int Delta => UnreadCount - PreviousUnreadCount;
    public bool HasUnreadMessages => UnreadCount > 0;

    public ChatUnreadCountChangedEvent(int previousUnreadCount, int unreadCount)
    {
        PreviousUnreadCount = NormalizeCount(previousUnreadCount);
        UnreadCount = NormalizeCount(unreadCount);
    }

    private static int NormalizeCount(int value)
    {
        return value < 0 ? 0 : value;
    }
}
