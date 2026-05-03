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

    [Header("Settings")]
    [SerializeField, Min(1)] private int maxStoredMessages = 80;
    [SerializeField, Min(1)] private int maxMessageLength = 120;
    [SerializeField, Min(0f)] private float messageCooldownSeconds = 0.5f;

    private readonly Dictionary<ulong, double> lastMessageTimeByClient = new Dictionary<ulong, double>();

    private NetworkList<ChatMessageData> messages;
    private bool isSubscribedToMessages;
    private bool isSubscribedToStateMachine;

    public event Action MessagesChanged;
    public event Action AvailabilityChanged;

    public bool CanSubmitMessages => ResolveCurrentChannel(out _);

    public ChatChannel CurrentChannel
    {
        get
        {
            if (ResolveCurrentChannel(out ChatChannel channel))
                return channel;

            return ChatChannel.Lobby;
        }
    }

    public int MessageCount => messages != null ? messages.Count : 0;

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

        ResolveReferences();
        SubscribeToMessages();
        SubscribeToStateMachine();

        SessionSpawned?.Invoke(this);
        AvailabilityChanged?.Invoke();
        MessagesChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeFromMessages();
        UnsubscribeFromStateMachine();

        lastMessageTimeByClient.Clear();

        if (Instance == this)
            Instance = null;

        SessionDespawned?.Invoke();
    }

    public override void OnDestroy()
    {
        messages?.Dispose();
        base.OnDestroy();
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
        if (!IsSpawned)
        {
            Debug.LogWarning("Cannot submit chat message before NetworkChatSession is spawned.");
            return;
        }

        if (!ResolveCurrentChannel(out ChatChannel channel))
            return;

        if (!TryNormalizeMessage(text, out string normalizedText))
            return;

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

        if (!IsClientConnected(senderClientId))
            return;

        if (!ResolveCurrentChannel(out ChatChannel serverChannel))
            return;

        if (requestedChannel != serverChannel)
            return;

        if (!TryNormalizeMessage(rawText.ToString(), out string normalizedText))
            return;

        if (!CanSendMessageNow(senderClientId))
            return;

        AppendMessage(
            senderClientId,
            ResolveSenderName(senderClientId),
            normalizedText,
            serverChannel
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
        normalizedText = string.Empty;

        if (string.IsNullOrWhiteSpace(rawText))
            return false;

        int safeMaxLength = Mathf.Clamp(maxMessageLength, 1, 120);

        string text = rawText
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();

        while (text.Contains("  "))
            text = text.Replace("  ", " ");

        if (text.Length > safeMaxLength)
            text = text.Substring(0, safeMaxLength).Trim();

        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text
            .Replace("<", "‹")
            .Replace(">", "›");

        normalizedText = text;
        return true;
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
        MessagesChanged?.Invoke();
    }

    private void HandleGameStateChanged(GameState previousState, GameState newState)
    {
        AvailabilityChanged?.Invoke();
        MessagesChanged?.Invoke();
    }
}