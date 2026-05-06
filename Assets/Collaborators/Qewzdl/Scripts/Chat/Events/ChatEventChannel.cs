using System;
using UnityEngine;

[CreateAssetMenu(fileName = "ChatEventChannel", menuName = "Chat/Event Channel")]
public class ChatEventChannel : ScriptableObject
{
    public event Action<ChatSendRequest> SendRequested;
    public event Action<ChatMessageReceivedEvent> MessageReceived;
    public event Action<ChatSendRejectedEvent> SendRejected;
    public event Action<ChatVisibilityChangedEvent> VisibilityChanged;
    public event Action<int> UnreadCountChanged;

    public bool RaiseSendRequested(ChatSendRequest request)
    {
        if (!request.IsValid(out string reason))
        {
            RaiseSendRejected(new ChatSendRejectedEvent(request, reason));
            return false;
        }

        if (SendRequested == null)
        {
            RaiseSendRejected(new ChatSendRejectedEvent(
                request,
                "Chat session is not ready."
            ));
            return false;
        }

        SendRequested.Invoke(request);
        return true;
    }

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

    public void RaiseUnreadCountChanged(int unreadCount)
    {
        UnreadCountChanged?.Invoke(Mathf.Max(0, unreadCount));
    }
}
