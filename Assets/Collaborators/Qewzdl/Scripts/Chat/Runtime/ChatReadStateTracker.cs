using UnityEngine;

public class ChatReadStateTracker : MonoBehaviour
{
    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    [Header("Settings")]
    [SerializeField] private bool countOwnMessagesAsUnread;

    public int UnreadCount { get; private set; }
    public bool IsChatOpen { get; private set; }

    private void OnEnable()
    {
        ResolveEventChannel();

        chatEvents.MessageReceived += OnMessageReceived;
        chatEvents.VisibilityChanged += OnVisibilityChanged;
        chatEvents.RaiseUnreadCountChanged(UnreadCount);
    }

    private void OnDisable()
    {
        if (chatEvents == null)
        {
            return;
        }

        chatEvents.MessageReceived -= OnMessageReceived;
        chatEvents.VisibilityChanged -= OnVisibilityChanged;
    }

    public void ResetUnreadCount()
    {
        SetUnreadCount(0);
    }

    private void OnMessageReceived(ChatMessageReceivedEvent messageEvent)
    {
        if (IsChatOpen)
        {
            return;
        }

        if (messageEvent.IsLocalSender && !countOwnMessagesAsUnread)
        {
            return;
        }

        SetUnreadCount(UnreadCount + 1);
    }

    private void OnVisibilityChanged(ChatVisibilityChangedEvent visibilityEvent)
    {
        IsChatOpen = visibilityEvent.IsOpen;

        if (IsChatOpen)
        {
            ResetUnreadCount();
        }
    }

    private void SetUnreadCount(int value)
    {
        int normalizedValue = Mathf.Max(0, value);

        if (UnreadCount == normalizedValue)
        {
            return;
        }

        UnreadCount = normalizedValue;
        chatEvents.RaiseUnreadCountChanged(UnreadCount);
    }

    private void ResolveEventChannel()
    {
        chatEvents = ChatEventChannel.Resolve(chatEvents);
    }
}
