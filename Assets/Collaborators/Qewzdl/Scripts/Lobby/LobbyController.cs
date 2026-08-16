using Unity.Netcode;
using UnityEngine;

public class LobbyController : NetworkBehaviour
{
    [SerializeField] private LobbyState lobbyState;
    [SerializeField] private LobbyConfig lobbyConfig;

    private INetworkSessionService sessionService;
    private INetworkSessionAdmissionService admissionService;

    private LobbyOwnershipService ownershipService;
    private LobbyPlayerRegistry playerRegistry;
    private LobbyPlayerCustomizationService playerCustomizationService;
    private LobbySettingsService settingsService;
    private LobbyStartService startService;
    private NetworkManager subscribedNetworkManager;
    private bool networkCallbacksSubscribed;

    private void Awake()
    {
        ResolveReferences();
    }

    public bool ValidateConfiguration()
    {
        ResolveReferences();

        bool valid = true;

        if (lobbyState == null)
        {
            Debug.LogError($"{nameof(LobbyController)} is missing {nameof(lobbyState)}.", this);
            valid = false;
        }

        if (lobbyConfig == null)
        {
            Debug.LogError($"{nameof(LobbyController)} is missing {nameof(lobbyConfig)}.", this);
            valid = false;
        }

        return valid;
    }

    public bool Construct(
        INetworkSessionService sessionService,
        INetworkSessionAdmissionService admissionService)
    {
        if (!ValidateConfiguration())
            return false;

        if (sessionService == null)
        {
            Debug.LogError("Network session service is missing.");
            return false;
        }

        if (admissionService == null)
        {
            Debug.LogError("Network session admission service is missing.");
            return false;
        }

        if (this.sessionService == sessionService &&
            this.admissionService == admissionService &&
            IsConstructed(false))
        {
            return true;
        }

        this.sessionService = sessionService;
        this.admissionService = admissionService;
        CreateServices();

        if (IsSpawned && IsServer)
            InitializeServerRuntime();

        return true;
    }

    public void Dispose()
    {
        UnsubscribeFromNetworkCallbacks();

        sessionService = null;
        admissionService = null;
        ownershipService = null;
        playerRegistry = null;
        playerCustomizationService = null;
        settingsService = null;
        startService = null;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        if (!IsConstructed()) return;

        InitializeServerRuntime();
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeFromNetworkCallbacks();
    }

    private void ResolveReferences()
    {
        if (lobbyState == null)
            lobbyState = GetComponent<LobbyState>();
    }

    private void CreateServices()
    {
        LobbyStartRules startRules = new LobbyStartRules();

        ownershipService = new LobbyOwnershipService(lobbyState);
        playerRegistry = new LobbyPlayerRegistry(
            lobbyState,
            ownershipService,
            admissionService);
        playerCustomizationService = new LobbyPlayerCustomizationService(lobbyState);
        settingsService = new LobbySettingsService(lobbyState, lobbyConfig);
        startService = new LobbyStartService(lobbyState, startRules, sessionService);
    }

    private void InitializeServerRuntime()
    {
        if (!IsConstructed() || NetworkManager == null)
            return;

        settingsService.InitializeFromConfig();
        PublishLobbyVisibility();
        SubscribeToNetworkCallbacks();

        // Everyone who is already here, not just the host. Coming back from a
        // finished match, nobody reconnects, so OnClientConnected never fires
        // again and a lobby built from that callback alone would list one
        // player while three stood in it.
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            playerRegistry.TryAddPlayer(clientId);
        }

        startService.RefreshCanStartGame();
    }

    // The settings hold the truth clients can see; approval needs the same
    // answer before a connection exists, so the server hands it over directly.
    private void PublishLobbyVisibility()
    {
        admissionService.SetAcceptingNewPlayers(lobbyState.Settings.Value.IsPublic);
    }

    private void SubscribeToNetworkCallbacks()
    {
        NetworkManager manager = NetworkManager;

        if (manager == null)
            return;

        if (networkCallbacksSubscribed && subscribedNetworkManager == manager)
            return;

        UnsubscribeFromNetworkCallbacks();

        subscribedNetworkManager = manager;
        subscribedNetworkManager.OnClientConnectedCallback += HandleClientConnected;
        subscribedNetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        networkCallbacksSubscribed = true;
    }

    private void UnsubscribeFromNetworkCallbacks()
    {
        if (!networkCallbacksSubscribed)
            return;

        if (subscribedNetworkManager != null)
        {
            subscribedNetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            subscribedNetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        subscribedNetworkManager = null;
        networkCallbacksSubscribed = false;
    }

    private bool IsConstructed()
    {
        return IsConstructed(true);
    }

    private bool IsConstructed(bool logMissing)
    {
        if (sessionService != null &&
            admissionService != null &&
            ownershipService != null &&
            playerRegistry != null &&
            playerCustomizationService != null &&
            settingsService != null &&
            startService != null)
        {
            return true;
        }

        if (logMissing)
            Debug.LogError("LobbyController was not constructed.");

        return false;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (!IsConstructed()) return;

        if (!playerRegistry.TryAddPlayer(clientId))
        {
            NetworkManager.DisconnectClient(clientId);
            return;
        }

        startService.RefreshCanStartGame();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (!IsConstructed()) return;

        // Idempotent with the global callback. Doing this before removing the
        // lobby entry guarantees the registry can capture reconnect state
        // regardless of NetworkManager callback subscription order.
        admissionService.RecordDisconnect(clientId);
        playerRegistry.RemovePlayer(clientId);
        startService.RefreshCanStartGame();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSetReadyRpc(bool isReady, RpcParams rpcParams = default)
    {
        if (!IsConstructed()) return;
        if (lobbyState.Phase.Value != LobbyPhase.Open) return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;

        playerCustomizationService.SetReady(senderClientId, isReady);
        startService.RefreshCanStartGame();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSetGameModeRpc(int gameModeId, RpcParams rpcParams = default)
    {
        if (!IsConstructed()) return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!ownershipService.CanChangeSettings(senderClientId)) return;

        settingsService.SetGameMode(gameModeId);
        startService.RefreshCanStartGame();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSetMapRpc(int mapId, RpcParams rpcParams = default)
    {
        if (!IsConstructed()) return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!ownershipService.CanChangeSettings(senderClientId)) return;

        settingsService.SetMap(mapId);
        startService.RefreshCanStartGame();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSetDifficultyRpc(int difficultyId, RpcParams rpcParams = default)
    {
        if (!IsConstructed()) return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!ownershipService.CanChangeSettings(senderClientId)) return;

        settingsService.SetDifficulty(difficultyId);
        startService.RefreshCanStartGame();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSetLobbyPublicRpc(bool isPublic, RpcParams rpcParams = default)
    {
        if (!IsConstructed()) return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!ownershipService.CanChangeSettings(senderClientId)) return;

        settingsService.SetLobbyPublic(isPublic);
        PublishLobbyVisibility();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestKickPlayerRpc(ulong targetClientId, RpcParams rpcParams = default)
    {
        if (!IsConstructed()) return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!ownershipService.CanChangeSettings(senderClientId)) return;
        if (targetClientId == senderClientId) return;

        // Only somebody standing in this lobby can be thrown out of it.
        if (!lobbyState.TryGetPlayerIndex(targetClientId, out _)) return;

        admissionService.KickPlayer(targetClientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestStartGameRpc(RpcParams rpcParams = default)
    {
        if (!IsConstructed()) return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!ownershipService.CanStartGame(senderClientId)) return;

        startService.TryStartGame();
    }

    public bool CanStartGame()
    {
        if (!IsServer) return false;
        if (!IsConstructed()) return false;

        return startService.CanStartGame();
    }

    public bool TryGetLocalClientId(out ulong clientId)
    {
        clientId = default;

        if (NetworkManager == null)
            return false;

        clientId = NetworkManager.LocalClientId;
        return true;
    }
}
