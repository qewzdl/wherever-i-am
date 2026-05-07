using UnityEngine;

public class ChatReadStateTracker : MonoBehaviour
{
    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    [Header("Settings")]
    [SerializeField] private bool countOwnMessagesAsUnread;
    [SerializeField] private bool countSystemMessagesAsUnread = true;

    private ChatVisibilityController visibilityController;

    public int UnreadCount { get; private set; }
    public bool IsChatOpen { get; private set; }

    private void OnEnable()
    {
        ResolveReferences();

        if (chatEvents == null)
        {
            Debug.LogError($"{nameof(ChatReadStateTracker)} requires an assigned {nameof(ChatEventChannel)}.", this);
            enabled = false;
            return;
        }

        SyncOpenState();

        chatEvents.MessageReceived += OnMessageReceived;
        chatEvents.VisibilityChanged += OnVisibilityChanged;
        PublishUnreadCount(chatEvents.CurrentUnreadCount);
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

    public void MarkAllAsRead()
    {
        SetUnreadCount(0);
    }

    private void OnMessageReceived(ChatMessageReceivedEvent messageEvent)
    {
        if (IsChatOpen || !ShouldCountMessage(messageEvent))
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
            MarkAllAsRead();
        }
    }

    private bool ShouldCountMessage(ChatMessageReceivedEvent messageEvent)
    {
        if (messageEvent.IsLocalSender && !countOwnMessagesAsUnread)
        {
            return false;
        }

        if (messageEvent.IsSystemMessage && !countSystemMessagesAsUnread)
        {
            return false;
        }

        return true;
    }

    private void SetUnreadCount(int value)
    {
        int nextUnreadCount = Mathf.Max(0, value);

        if (UnreadCount == nextUnreadCount)
        {
            return;
        }

        int previousUnreadCount = UnreadCount;
        UnreadCount = nextUnreadCount;

        PublishUnreadCount(previousUnreadCount);
    }

    private void PublishUnreadCount(int previousUnreadCount)
    {
        chatEvents.RaiseUnreadCountChanged(new ChatUnreadCountChangedEvent(
            previousUnreadCount,
            UnreadCount
        ));
    }

    private void SyncOpenState()
    {
        IsChatOpen = visibilityController != null && visibilityController.IsOpen;

        if (IsChatOpen)
        {
            UnreadCount = 0;
        }
    }

    private void ResolveReferences()
    {
        if (visibilityController == null)
        {
            visibilityController = GetComponent<ChatVisibilityController>();
        }
    }
}
