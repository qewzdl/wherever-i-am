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

    private bool isSubscribed;

    public void SetEventChannel(ChatEventChannel chatEvents)
    {
        bool shouldSubscribe = isActiveAndEnabled;
        Unsubscribe();

        this.chatEvents = chatEvents;

        if (shouldSubscribe)
        {
            Subscribe();
        }
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    public void MarkAllAsRead()
    {
        SetUnreadCount(0);
    }

    private void Subscribe()
    {
        if (isSubscribed)
        {
            return;
        }

        ResolveReferences();

        if (chatEvents == null)
        {
            return;
        }

        SyncOpenState();

        chatEvents.MessageReceived += OnMessageReceived;
        chatEvents.VisibilityChanged += OnVisibilityChanged;
        isSubscribed = true;

        SyncUnreadCountFromEventChannel();
    }

    private void Unsubscribe()
    {
        if (!isSubscribed || chatEvents == null)
        {
            isSubscribed = false;
            return;
        }

        chatEvents.MessageReceived -= OnMessageReceived;
        chatEvents.VisibilityChanged -= OnVisibilityChanged;
        isSubscribed = false;
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
        if (chatEvents == null)
        {
            return;
        }

        chatEvents.RaiseUnreadCountChanged(new ChatUnreadCountChangedEvent(
            previousUnreadCount,
            UnreadCount
        ));
    }

    private void SyncUnreadCountFromEventChannel()
    {
        if (chatEvents == null)
        {
            return;
        }

        UnreadCount = chatEvents.CurrentUnreadCount;

        if (IsChatOpen)
        {
            SetUnreadCount(0);
        }
    }

    private void SyncOpenState()
    {
        IsChatOpen = visibilityController != null && visibilityController.IsOpen;
    }

    private void ResolveReferences()
    {
        if (visibilityController == null)
        {
            visibilityController = GetComponent<ChatVisibilityController>();
        }
    }
}
