using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class NetworkChatSession : NetworkBehaviour,
    IChatReadService,
    IChatCommandService,
    ISessionServiceReadiness
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
    [SerializeField] private string playerKickedMessageFormat =
        "{0} was removed by the host.";
    [SerializeField] private string playerLostConnectionMessageFormat =
        "{0} lost connection.";
    [SerializeField] private string playerCameBackMessageFormat =
        "{0} came back.";

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
    private IDisposable serviceRegistrations;
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

    bool ISessionServiceReadiness.IsSessionServiceReady =>
        IsSpawned &&
        isActiveAndEnabled &&
        messages != null &&
        (!IsServer || stateMachine != null);

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
        AddSystemMessage(text, string.Empty);
    }

    /// <summary>
    /// A system line, and the name it is about if it is about anybody.
    /// </summary>
    /// <remarks>
    /// The name is carried beside the sentence rather than inside it so that
    /// the sentence is still one a locale table can answer for. Whoever draws
    /// it puts the two together, in whatever language they are reading.
    /// </remarks>
    public void AddSystemMessage(string text, string about)
    {
        if (!IsServer)
            return;

        if (!TryNormalizeMessage(text, out string normalizedText))
            return;

        AppendMessage(
            0,
            string.IsNullOrWhiteSpace(about) ? "System" : about,
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
        return PlayerDisplayName.Resolve(clientId);
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

        // Coming back is not arriving. The seat was theirs the whole time,
        // and the room was told they might return.
        AddConnectionSystemMessage(
            clientId,
            IsReconnectingClient(clientId)
                ? playerCameBackMessageFormat
                : playerJoinedMessageFormat);
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        lastMessageTimeByClient.Remove(clientId);

        if (!CanAnnounceConnectionForClient(clientId))
            return;

        // Leaving and being thrown out look the same from here, and telling
        // the room somebody left when the host removed them is the part that
        // reads as a bug to everyone watching.
        AddConnectionSystemMessage(clientId, ResolveDisconnectFormat(clientId));
    }

    // Three different things arrive here as one event. Somebody removed by
    // the host is not somebody who left; and somebody whose seat is being held
    // for the next twenty seconds has not left either - saying so is what
    // makes the room wait for them instead of starting without them.
    private string ResolveDisconnectFormat(ulong clientId)
    {
        if (WasKickedFromSession(clientId))
            return playerKickedMessageFormat;

        return HoldsSeatForDisconnects()
            ? playerLostConnectionMessageFormat
            : playerLeftMessageFormat;
    }

    private static bool HoldsSeatForDisconnects()
    {
        return G.TryResolve(out INetworkSessionAdmissionService admissionService) &&
               admissionService.HoldsSeatsForDisconnects;
    }

    private static bool IsReconnectingClient(ulong clientId)
    {
        return G.TryResolve(out INetworkSessionAdmissionService admissionService) &&
               admissionService.IsReconnect(clientId);
    }

    private bool WasKickedFromSession(ulong clientId)
    {
        return G.TryResolve(out INetworkSessionAdmissionService admissionService) &&
               admissionService.WasKicked(clientId);
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

    // The format and the name, not the sentence they make.
    //
    // These used to be joined here and broadcast finished, which put the one
    // kind of message nobody could translate into every player's chat: the
    // client received "Bob joined the game." and no table has ever heard of
    // that - the row says "{0} joined the game." and the name is different
    // every time.
    //
    // Nothing was added to the wire to fix it. A system message carries a
    // sender name that the chat window has always ignored, because the room
    // speaking gets no name, so the name travels there and the sentence stays
    // a sentence a table can answer for.
    private void AddConnectionSystemMessage(ulong clientId, string messageFormat)
    {
        if (string.IsNullOrWhiteSpace(messageFormat))
        {
            Debug.LogError(
                $"{nameof(NetworkChatSession)} connection notification format is empty.",
                this);

            return;
        }

        AddSystemMessage(messageFormat, ResolveSenderName(clientId));
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

        return NetworkObjectServiceContext.TryRegisterRequiredSessionServices(
            this,
            registration =>
            {
                registration.Register<IChatReadService>(this);
                registration.Register<IChatCommandService>(this);
            },
            out serviceRegistrations);
    }

    private void UnregisterSessionServices()
    {
        IDisposable registrations = serviceRegistrations;
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
