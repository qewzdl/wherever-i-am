using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class NetworkChatSession : NetworkBehaviour, IChatReadService, IChatCommandService
{
    [Header("References")]
    [SerializeField] private GameStateMachine stateMachine;

    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    [Header("Settings")]
    [SerializeField, Min(1)] private int maxStoredMessages = 80;
    [SerializeField, Min(1)] private int maxMessageLength = 120;
    [SerializeField, Min(0f)] private float messageCooldownSeconds = 0.5f;

    [Header("Connection Notifications")]
    [SerializeField] private bool announcePlayerConnections = true;
    [SerializeField] private string playerJoinedMessageFormat = "{0} joined the game.";
    [SerializeField] private string playerLeftMessageFormat = "{0} left the game.";

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
    private SessionServiceRegistration serviceRegistrations;
    private bool isSubscribedToMessages;
    private bool isSubscribedToStateMachine;
    private bool isSubscribedToConnectionCallbacks;
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

    }

    public override void OnNetworkSpawn()
    {
        DontDestroyOnLoad(gameObject);

        if (IsServer)
        {
            if (stateMachine == null)
            {
                Debug.LogError(
                    $"{nameof(NetworkChatSession)} server instance was not constructed with " +
                    $"{nameof(GameStateMachine)}.",
                    this);
            }

            RefreshAvailabilityFromState();
            SubscribeToStateMachine();
            SubscribeToConnectionCallbacks();
        }

        SubscribeToMessages();

        currentChannel.OnValueChanged += HandleCurrentChannelChanged;
        isChatAvailable.OnValueChanged += HandleAvailabilityChanged;

        if (!RegisterSessionServices())
        {
            enabled = false;
            return;
        }

        AvailabilityChanged?.Invoke();
        MessagesChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        UnregisterSessionServices();
        UnsubscribeFromMessages();
        UnsubscribeFromStateMachine();
        UnsubscribeFromConnectionCallbacks();

        lastMessageTimeByClient.Clear();

        currentChannel.OnValueChanged -= HandleCurrentChannelChanged;
        isChatAvailable.OnValueChanged -= HandleAvailabilityChanged;
    }

    public override void OnDestroy()
    {
        UnregisterSessionServices();
        UnsubscribeFromMessages();
        UnsubscribeFromStateMachine();
        UnsubscribeFromConnectionCallbacks();

        messages?.Dispose();

        base.OnDestroy();
    }

    private void RefreshAvailabilityFromState()
    {
        if (!IsServer)
            return;

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

    public bool TryGetMessage(
        uint messageId,
        out ChatMessageData message)
    {
        message = default;

        if (messageId == 0 || messages == null)
        {
            return false;
        }

        for (int i = messages.Count - 1; i >= 0; i--)
        {
            ChatMessageData candidate = messages[i];

            if (candidate.MessageId != messageId)
            {
                continue;
            }

            message = candidate;
            return true;
        }

        return false;
    }

    public bool IsLocalClient(ulong clientId)
    {
        return NetworkManager != null &&
               NetworkManager.IsListening &&
               NetworkManager.LocalClientId == clientId;
    }

    public void SubmitMessage(string text)
    {
        HandleSendRequested(new ChatSendRequest(text, CurrentChannel.ToString()));
    }

    private void HandleSendRequested(ChatSendRequest request)
    {
        if (!IsClient)
        {
            return;
        }

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

        if (!TryNormalizeMessage(request.GetNormalizedText(), out string normalizedText, out string reason))
        {
            RaiseLocalSendRejected(request, reason);
            return;
        }

        ChatChannel requestedChannel = currentChannel.Value;
        SubmitMessageRpc(new FixedString512Bytes(normalizedText), requestedChannel);
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

    private void SubscribeToConnectionCallbacks()
    {
        if (isSubscribedToConnectionCallbacks)
            return;

        if (!IsServer)
            return;

        if (NetworkManager == null)
        {
            Debug.LogError($"{nameof(NetworkChatSession)} requires an active {nameof(NetworkManager)} to announce player connections.", this);
            return;
        }

        NetworkManager.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        isSubscribedToConnectionCallbacks = true;
    }

    private void UnsubscribeFromConnectionCallbacks()
    {
        if (!isSubscribedToConnectionCallbacks)
            return;

        if (NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        isSubscribedToConnectionCallbacks = false;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (!CanAnnounceConnectionForClient(clientId))
            return;

        AddConnectionSystemMessage(clientId, playerJoinedMessageFormat);
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        lastMessageTimeByClient.Remove(clientId);

        if (!CanAnnounceConnectionForClient(clientId))
            return;

        AddConnectionSystemMessage(clientId, playerLeftMessageFormat);
    }

    private bool CanAnnounceConnectionForClient(ulong clientId)
    {
        if (!IsServer)
            return false;

        if (!announcePlayerConnections)
            return false;

        if (NetworkManager == null || !NetworkManager.IsListening)
            return false;

        return clientId != NetworkManager.ServerClientId;
    }

    private void AddConnectionSystemMessage(ulong clientId, string messageFormat)
    {
        if (!TryCreateConnectionSystemMessage(clientId, messageFormat, out string message))
            return;

        AddSystemMessage(message);
    }

    private bool TryCreateConnectionSystemMessage(
        ulong clientId,
        string messageFormat,
        out string message)
    {
        message = string.Empty;

        if (string.IsNullOrWhiteSpace(messageFormat))
        {
            Debug.LogError($"{nameof(NetworkChatSession)} connection notification format is empty.", this);
            return false;
        }

        try
        {
            message = string.Format(messageFormat, ResolveSenderName(clientId));
        }
        catch (FormatException exception)
        {
            Debug.LogError($"{nameof(NetworkChatSession)} connection notification format is invalid: {exception.Message}", this);
            return false;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            Debug.LogError($"{nameof(NetworkChatSession)} connection notification message is empty.", this);
            return false;
        }

        return true;
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

    private bool RegisterSessionServices()
    {
        if (serviceRegistrations != null)
            return true;

        if (NetworkManager == null)
        {
            Debug.LogError(
                $"{nameof(NetworkChatSession)} cannot register without an active {nameof(NetworkManager)}.",
                this);

            return false;
        }

        NetworkSessionOrchestrator orchestrator =
            NetworkManager.GetComponent<NetworkSessionOrchestrator>();

        if (orchestrator == null)
        {
            Debug.LogError(
                $"{nameof(NetworkChatSession)} requires {nameof(NetworkSessionOrchestrator)} " +
                $"on the {nameof(NetworkManager)} object.",
                this);

            return false;
        }

        if (orchestrator.TryRegisterSessionServices(
                registrar =>
                {
                    registrar.Register<IChatReadService>(this);
                    registrar.Register<IChatCommandService>(this);
                },
                out serviceRegistrations,
                out Exception failure))
        {
            return true;
        }

        Debug.LogError(
            $"{nameof(NetworkChatSession)} failed to register chat contracts in the Session scope.",
            this);

        if (failure != null)
            Debug.LogException(failure, this);

        _ = orchestrator.ReportSessionReadinessFailureAsync(
            nameof(NetworkChatSession),
            failure != null
                ? failure.Message
                : "Failed to register both chat contracts.");

        return false;
    }

    private void UnregisterSessionServices()
    {
        SessionServiceRegistration registrations = serviceRegistrations;
        serviceRegistrations = null;

        if (registrations == null)
            return;

        try
        {
            registrations.Dispose();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
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
