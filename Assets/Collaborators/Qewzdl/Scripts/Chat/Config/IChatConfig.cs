public interface IChatConfig
{
    int MaxStoredMessages { get; }

    int MaxMessageLength { get; }

    float MessageCooldownSeconds { get; }
}