using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class NetworkChatSession : NetworkBehaviour, IChatReadService, IChatCommandService
{
    public static NetworkChatSession Instance { get; private set; }

    public static event Action<NetworkChatSession> SessionSpawned;
    public static event Action SessionDespawned;

    [Header("References")]
    [SerializeField] private GameStateMachine stateMachine;

    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    [Header("Settings")]
    [SerializeField, Min(1)] private int maxStoredMessages = 80;
    [SerializeField, Min(1)] private int maxMessageLength = 120;
    [SerializeField, Min(0f)] private float messageCooldownSeconds = 0.5f;

    private readonly Dictionary<ulong, double> lastMessageTimeByClient = new Dictionary<ulong, double>();
    private readonly ChatMessageValidator messageValidator = new ChatMessageValidator();

    private readonly NetworkVariable<ChatChannel> currentChannel = new NetworkVariable<ChatChannel>(
        ChatChannel.Lobby,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<bool> isChatAvailable = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkList<ChatMessageData> messages;
    private bool isSubscribedToMessages;
    private bool isSubscribedToStateMachine;
    private bool isSubscribedToEventChannel;
    private uint nextMessageId = 1;

    public event Action MessagesChanged;
    public event Action<ChatMessageData> MessageAdded;
    public event Action AvailabilityChanged;

    public bool CanSubmitMessages => IsSpawned && isChatAvailable.Value;

    public ChatChannel CurrentChannel => currentChannel.Value;

    public int MessageCount => messages != null ? messages.Count : 0;

    public void Construct(GameStateMachine injectedStateMachine, IChatConfig config)
    {
        if (injectedStateMachine != null)
            stateMachine = injectedStateMachine;

        ApplyConfig(config);
    }

    private void Awake()
    {
        messages = new NetworkList<ChatMessageData>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        ResolveReferences();
    }

    public override void OnNetworkSpawn()
    {
        DontDestroyOnLoad(gameObject);

        Instance = this;

        if (IsServer)
        {
            ResolveReferences();
            RefreshAvailabilityFromState();
            SubscribeToStateMachine();
        }

        SubscribeToMessages();
        SubscribeToEventChannel();

        currentChannel.OnValueChanged += HandleCurrentChannelChanged;
        isChatAvailable.OnValueChanged += HandleAvailabilityChanged;

        SessionSpawned?.Invoke(this);
        AvailabilityChanged?.Invoke();
        MessagesChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeFromEventChannel();
        UnsubscribeFromMessages();
        UnsubscribeFromStateMachine();

        lastMessageTimeByClient.Clear();

        if (Instance == this)
            Instance = null;

        currentChannel.OnValueChanged -= HandleCurrentChannelChanged;
        isChatAvailable.OnValueChanged -= HandleAvailabilityChanged;

        SessionDespawned?.Invoke();
    }

    public override void OnDestroy()
    {
        UnsubscribeFromEventChannel();
        messages?.Dispose();
        base.OnDestroy();
    }

    private void RefreshAvailabilityFromState()
    {
        if (!IsServer)
            return;

        if (stateMachine == null)
            ResolveReferences();

        if (stateMachine == null)
        {
            isChatAvailable.Value = false;
            return;
        }

        switch (stateMachine.CurrentState)
        {
            case GameState.Lobby:
                currentChannel.Value = ChatChannel.Lobby;
                isChatAvailable.Value = true;
                break;

            case GameState.InGame:
                currentChannel.Value = ChatChannel.Game;
                isChatAvailable.Value = true;
                break;

            default:
                isChatAvailable.Value = false;
                break;
        }
    }

    private void HandleCurrentChannelChanged(ChatChannel previousValue, ChatChannel newValue)
    {
        AvailabilityChanged?.Invoke();
        MessagesChanged?.Invoke();
    }

    private void HandleAvailabilityChanged(bool previousValue, bool newValue)
    {
        AvailabilityChanged?.Invoke();
        MessagesChanged?.Invoke();
    }

    public ChatMessageData GetMessage(int index)
    {
        if (messages == null)
        {
            Debug.LogError("Chat messages list is missing.");
            return default;
        }

        if (index < 0 || index >= messages.Count)
        {
            Debug.LogError($"Chat message index out of range: {index}");
            return default;
        }

        return messages[index];
    }

    public void SubmitMessage(string text)
    {
        HandleSendRequested(new ChatSendRequest(text, CurrentChannel.ToString()));
    }

    private void HandleSendRequested(ChatSendRequest request)
    {
        if (!IsSpawned)
        {
            RaiseLocalSendRejected(request, "Chat session is not ready.");
            return;
        }

        if (!CanSubmitMessages)
        {
            RaiseLocalSendRejected(request, "Chat is not available.");
            return;
        }

        if (!ResolveCurrentChannel(out ChatChannel channel))
        {
            RaiseLocalSendRejected(request, "Chat is not available.");
            return;
        }

        if (!TryNormalizeMessage(request.GetNormalizedText(), out string normalizedText, out string reason))
        {
            RaiseLocalSendRejected(request, reason);
            return;
        }

        SubmitMessageRpc(new FixedString512Bytes(normalizedText), channel);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SubmitMessageRpc(
        FixedString512Bytes rawText,
        ChatChannel requestedChannel,
        RpcParams rpcParams = default)
    {
        if (!IsServer)
            return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        string submittedText = rawText.ToString();

        if (!IsClientConnected(senderClientId))
            return;

        if (!ResolveCurrentChannel(out ChatChannel serverChannel))
        {
            RejectMessage(senderClientId, submittedText, "Chat is not available.");
            return;
        }

        if (requestedChannel != serverChannel)
        {
            RejectMessage(senderClientId, submittedText, "Chat channel changed. Try again.");
            return;
        }

        if (!TryNormalizeMessage(submittedText, out string normalizedText, out string reason))
        {
            RejectMessage(senderClientId, submittedText, reason);
            return;
        }

        if (!CanSendMessageNow(senderClientId))
        {
            RejectMessage(senderClientId, normalizedText, "You are sending messages too quickly.");
            return;
        }

        AppendMessage(
            senderClientId,
            ResolveSenderName(senderClientId),
            normalizedText,
            serverChannel
        );
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void RejectMessageRpc(
        FixedString512Bytes rawText,
        FixedString128Bytes reason,
        RpcParams rpcParams = default)
    {
        RaiseLocalSendRejected(
            new ChatSendRequest(rawText.ToString(), CurrentChannel.ToString()),
            reason.ToString()
        );
    }

    public void AddSystemMessage(string text)
    {
        if (!IsServer)
            return;

        if (!TryNormalizeMessage(text, out string normalizedText))
            return;

        AppendMessage(
            0,
            "System",
            normalizedText,
            ChatChannel.System
        );
    }

    private void AppendMessage(
        ulong senderClientId,
        string senderName,
        string text,
        ChatChannel channel)
    {
        if (messages == null)
            return;

        int safeMaxStoredMessages = Mathf.Max(1, maxStoredMessages);

        while (messages.Count >= safeMaxStoredMessages)
            messages.RemoveAt(0);

        double serverTime = NetworkManager != null
            ? NetworkManager.ServerTime.Time
            : Time.unscaledTimeAsDouble;

        messages.Add(new ChatMessageData(
            GetNextMessageId(),
            senderClientId,
            senderName,
            text,
            channel,
            serverTime
        ));
    }

    private bool ResolveCurrentChannel(out ChatChannel channel)
    {
        channel = ChatChannel.Lobby;

        if (stateMachine == null)
            ResolveReferences();

        if (stateMachine == null)
            return false;

        switch (stateMachine.CurrentState)
        {
            case GameState.Lobby:
                channel = ChatChannel.Lobby;
                return true;

            case GameState.InGame:
                channel = ChatChannel.Game;
                return true;

            default:
                return false;
        }
    }

    private bool IsClientConnected(ulong clientId)
    {
        return NetworkManager != null &&
               NetworkManager.ConnectedClients.ContainsKey(clientId);
    }

    private bool CanSendMessageNow(ulong clientId)
    {
        if (messageCooldownSeconds <= 0f)
            return true;

        double now = NetworkManager != null
            ? NetworkManager.ServerTime.Time
            : Time.unscaledTimeAsDouble;

        if (lastMessageTimeByClient.TryGetValue(clientId, out double lastSendTime))
        {
            if (now - lastSendTime < messageCooldownSeconds)
                return false;
        }

        lastMessageTimeByClient[clientId] = now;
        return true;
    }

    private string ResolveSenderName(ulong clientId)
    {
        return $"Player {clientId}";
    }

    private bool TryNormalizeMessage(string rawText, out string normalizedText)
    {
        return TryNormalizeMessage(rawText, out normalizedText, out _);
    }

    private bool TryNormalizeMessage(
        string rawText,
        out string normalizedText,
        out string rejectionReason)
    {
        if (messageValidator.TryNormalize(rawText, maxMessageLength, out normalizedText))
        {
            rejectionReason = string.Empty;
            return true;
        }

        rejectionReason = "Message is empty.";
        return false;
    }

    private uint GetNextMessageId()
    {
        uint messageId = nextMessageId;
        nextMessageId++;

        if (nextMessageId == 0)
            nextMessageId = 1;

        return messageId;
    }

    private void ApplyConfig(IChatConfig config)
    {
        if (config == null)
            return;

        maxStoredMessages = config.MaxStoredMessages;
        maxMessageLength = config.MaxMessageLength;
        messageCooldownSeconds = config.MessageCooldownSeconds;
    }

    private void ResolveReferences()
    {
        if (stateMachine == null)
            stateMachine = FindFirstObjectByType<GameStateMachine>();
    }

    private void SubscribeToMessages()
    {
        if (isSubscribedToMessages || messages == null)
            return;

        messages.OnListChanged += HandleMessagesChanged;
        isSubscribedToMessages = true;
    }

    private void UnsubscribeFromMessages()
    {
        if (!isSubscribedToMessages || messages == null)
            return;

        messages.OnListChanged -= HandleMessagesChanged;
        isSubscribedToMessages = false;
    }

    private void SubscribeToStateMachine()
    {
        if (isSubscribedToStateMachine)
            return;

        if (stateMachine == null)
            ResolveReferences();

        if (stateMachine == null)
            return;

        stateMachine.StateChanged += HandleGameStateChanged;
        isSubscribedToStateMachine = true;
    }

    private void UnsubscribeFromStateMachine()
    {
        if (!isSubscribedToStateMachine || stateMachine == null)
            return;

        stateMachine.StateChanged -= HandleGameStateChanged;
        isSubscribedToStateMachine = false;
    }

    private void HandleMessagesChanged(NetworkListEvent<ChatMessageData> changeEvent)
    {
        if (changeEvent.Type == NetworkListEvent<ChatMessageData>.EventType.Add)
        {
            MessageAdded?.Invoke(changeEvent.Value);
            RaiseMessageReceived(changeEvent.Value);
        }

        MessagesChanged?.Invoke();
    }

    private void HandleGameStateChanged(GameState previousState, GameState newState)
    {
        RefreshAvailabilityFromState();

        AvailabilityChanged?.Invoke();
        MessagesChanged?.Invoke();
    }

    private void SubscribeToEventChannel()
    {
        if (isSubscribedToEventChannel)
            return;

        if (!IsClient)
            return;

        if (chatEvents == null)
        {
            Debug.LogError($"{nameof(NetworkChatSession)} requires an assigned {nameof(ChatEventChannel)}.", this);
            return;
        }

        chatEvents.SendRequested += HandleSendRequested;
        isSubscribedToEventChannel = true;
    }

    private void UnsubscribeFromEventChannel()
    {
        if (!isSubscribedToEventChannel || chatEvents == null)
            return;

        chatEvents.SendRequested -= HandleSendRequested;
        isSubscribedToEventChannel = false;
    }

    private void RaiseMessageReceived(ChatMessageData message)
    {
        if (!IsClient)
            return;

        if (chatEvents == null)
            return;

        bool isSystemMessage = message.Channel == ChatChannel.System;
        bool isLocalSender = !isSystemMessage &&
                             NetworkManager != null &&
                             NetworkManager.IsListening &&
                             message.SenderClientId == NetworkManager.LocalClientId;

        chatEvents.RaiseMessageReceived(new ChatMessageReceivedEvent(
            message.MessageId.ToString(),
            message.Channel.ToString(),
            message.SenderClientId,
            message.SenderName.ToString(),
            message.Text.ToString(),
            isLocalSender,
            isSystemMessage,
            message.ServerTime
        ));
    }

    private void RaiseLocalSendRejected(ChatSendRequest request, string reason)
    {
        if (!IsClient)
            return;

        if (chatEvents == null)
            return;

        chatEvents.RaiseSendRejected(new ChatSendRejectedEvent(request, reason));
    }

    private void RejectMessage(ulong clientId, string rawText, string reason)
    {
        RejectMessageRpc(
            new FixedString512Bytes(rawText ?? string.Empty),
            new FixedString128Bytes(reason ?? "Message was rejected."),
            RpcTarget.Single(clientId, RpcTargetUse.Temp)
        );
    }
}
