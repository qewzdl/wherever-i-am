using System;

public interface IChatReadService
{
    event Action MessagesChanged;
    event Action AvailabilityChanged;

    bool CanSubmitMessages { get; }
    ChatChannel CurrentChannel { get; }
    int MessageCount { get; }

    ChatMessageData GetMessage(int index);
}