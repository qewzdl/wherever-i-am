using System;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Events;

public class ChatNotificationController : MonoBehaviour
{
    [Header("References")]
    [FormerlySerializedAs("chatWindow")]
    [SerializeField] private MonoBehaviour chatWindowBehaviour;

    private IChatWindowView chatWindow;

    [Header("Settings")]
    [SerializeField] private bool ignoreLocalPlayerMessages = true;

    [Header("Events")]
    [SerializeField] private UnityEvent messageReceivedWhileOpen;
    [SerializeField] private UnityEvent messageReceivedWhileClosed;

    private IChatReadService readService;
    private int unreadCount;

    public event Action<int> UnreadCountChanged;

    public int UnreadCount => unreadCount;

    public void Construct(IChatReadService readService, IChatWindowView chatWindow)
    {
        Unsubscribe();

        this.readService = readService;

        if (chatWindow != null)
        {
            this.chatWindow = chatWindow;
            chatWindowBehaviour = chatWindow as MonoBehaviour;
        }

        if (this.chatWindow == null)
            this.chatWindow = chatWindowBehaviour as IChatWindowView;

        if (isActiveAndEnabled)
            Subscribe();
        ClearUnreadCount();
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (readService != null)
            readService.MessageAdded += HandleMessageAdded;

        if (chatWindow != null)
            chatWindow.Opened += HandleChatOpened;
    }

    private void Unsubscribe()
    {
        if (readService != null)
            readService.MessageAdded -= HandleMessageAdded;

        if (chatWindow != null)
            chatWindow.Opened -= HandleChatOpened;
    }

    private void HandleMessageAdded(ChatMessageData message)
    {
        if (ShouldIgnoreMessage(message))
            return;

        bool isOpen = chatWindow != null && chatWindow.IsOpen;

        if (isOpen)
        {
            messageReceivedWhileOpen?.Invoke();
            return;
        }

        unreadCount++;
        UnreadCountChanged?.Invoke(unreadCount);
        messageReceivedWhileClosed?.Invoke();
    }

    private bool ShouldIgnoreMessage(ChatMessageData message)
    {
        if (!ignoreLocalPlayerMessages)
            return false;

        if (message.Channel == ChatChannel.System)
            return false;

        if (readService == null)
            return false;

        return readService.IsLocalClient(message.SenderClientId);
    }

    private void HandleChatOpened()
    {
        ClearUnreadCount();
    }

    private void ClearUnreadCount()
    {
        if (unreadCount == 0)
            return;

        unreadCount = 0;
        UnreadCountChanged?.Invoke(unreadCount);
    }
}
