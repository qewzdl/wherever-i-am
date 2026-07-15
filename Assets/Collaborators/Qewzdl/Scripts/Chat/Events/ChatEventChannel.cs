using System;
using UnityEngine;

[CreateAssetMenu(fileName = "ChatEventChannel", menuName = "Wherever I Am/Chat/Event Channel")]
public class ChatEventChannel : ScriptableObject
{
    public event Action<ChatMessageReceivedEvent> MessageReceived;
    public event Action<ChatSendRejectedEvent> SendRejected;
    public event Action<ChatVisibilityChangedEvent> VisibilityChanged;
    public event Action<ChatUnreadCountChangedEvent> UnreadCountChanged;

    public int CurrentUnreadCount { get; private set; }

    public void RaiseMessageReceived(ChatMessageReceivedEvent messageEvent)
    {
        if (string.IsNullOrWhiteSpace(messageEvent.Text))
        {
            return;
        }

        MessageReceived?.Invoke(messageEvent);
    }

    public void RaiseSendRejected(ChatSendRejectedEvent rejectedEvent)
    {
        SendRejected?.Invoke(rejectedEvent);
    }

    public void RaiseVisibilityChanged(ChatVisibilityChangedEvent visibilityEvent)
    {
        VisibilityChanged?.Invoke(visibilityEvent);
    }

    public void RaiseUnreadCountChanged(ChatUnreadCountChangedEvent unreadEvent)
    {
        CurrentUnreadCount = unreadEvent.UnreadCount;
        UnreadCountChanged?.Invoke(unreadEvent);
    }

    public void RaiseUnreadCountChanged(int unreadCount)
    {
        RaiseUnreadCountChanged(new ChatUnreadCountChangedEvent(
            CurrentUnreadCount,
            Mathf.Max(0, unreadCount)
        ));
    }
}
