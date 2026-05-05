using System;
using Unity.Netcode;
using UnityEngine;

public sealed class ChatUnreadMessageNotifier : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ChatWindowUI chatWindow;

    [Header("Settings")]
    [SerializeField] private bool ignoreOwnMessages = true;
    [SerializeField] private bool notifyAboutSystemMessages = true;

    private IChatReadService readService;
    private bool isSubscribed;

    public event Action<ChatMessageData> MessageReceivedWhileClosed;
    public event Action<int> UnreadCountChanged;

    public int UnreadCount { get; private set; }

    public void Construct(IChatReadService readService, ChatWindowUI chatWindow)
    {
        Unsubscribe();

        this.readService = readService;
        this.chatWindow = chatWindow;

        Subscribe();
    }

    public void MarkAllAsRead()
    {
        if (UnreadCount == 0)
            return;

        UnreadCount = 0;
        UnreadCountChanged?.Invoke(UnreadCount);
    }

    private void Awake()
    {
        if (chatWindow == null)
            chatWindow = GetComponent<ChatWindowUI>();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (isSubscribed)
            return;

        if (readService != null)
            readService.MessageAdded += HandleMessageAdded;

        if (chatWindow != null)
            chatWindow.Opened += HandleChatOpened;

        isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!isSubscribed)
            return;

        if (readService != null)
            readService.MessageAdded -= HandleMessageAdded;

        if (chatWindow != null)
            chatWindow.Opened -= HandleChatOpened;

        isSubscribed = false;
    }

    private void HandleMessageAdded(ChatMessageData message)
    {
        if (chatWindow == null)
            return;

        if (chatWindow.IsOpen)
            return;

        if (!ShouldNotifyAboutMessage(message))
            return;

        UnreadCount++;

        MessageReceivedWhileClosed?.Invoke(message);
        UnreadCountChanged?.Invoke(UnreadCount);
    }

    private void HandleChatOpened()
    {
        MarkAllAsRead();
    }

    private bool ShouldNotifyAboutMessage(ChatMessageData message)
    {
        if (message.Channel == ChatChannel.System)
            return notifyAboutSystemMessages;

        if (ignoreOwnMessages && IsOwnMessage(message))
            return false;

        if (readService == null)
            return true;

        return message.Channel == readService.CurrentChannel;
    }

    private bool IsOwnMessage(ChatMessageData message)
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        return networkManager != null &&
               networkManager.IsListening &&
               message.SenderClientId == networkManager.LocalClientId;
    }
}