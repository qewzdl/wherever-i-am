using Unity.Netcode;
using UnityEngine;

public class LobbyController : NetworkBehaviour
{
    [SerializeField] private LobbyState lobbyState;
    [SerializeField] private LobbyConfig lobbyConfig;

    [Header("Session")]
    [SerializeField] private NetworkSessionOrchestrator networkSessionOrchestrator;

    private INetworkSessionService sessionService;

    private LobbyOwnershipService ownershipService;
    private LobbyPlayerRegistry playerRegistry;
    private LobbyPlayerCustomizationService playerCustomizationService;
    private LobbySettingsService settingsService;
    private LobbyStartService startService;

    private void Awake()
    {
        ResolveReferences();
        CreateServices();
    }

    public void Construct(INetworkSessionService sessionService)
    {
        this.sessionService = sessionService;
        CreateServices();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;

        settingsService.InitializeFromConfig(lobbyConfig);

        NetworkManager.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

        playerRegistry.AddPlayerIfNotExists(NetworkManager.LocalClientId);
        startService.RefreshCanStartGame();
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager == null || !IsServer)
            return;

        NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    private void ResolveReferences()
    {
        if (lobbyState == null)
            lobbyState = GetComponent<LobbyState>();

        if (lobbyConfig == null)
            Debug.LogError("LobbyConfig is not assigned.");

        if (networkSessionOrchestrator == null)
            networkSessionOrchestrator = FindFirstObjectByType<NetworkSessionOrchestrator>();

        if (sessionService == null)
            sessionService = networkSessionOrchestrator;
    }

    private void CreateServices()
    {
        LobbyStartRules startRules = new LobbyStartRules();

        ownershipService = new LobbyOwnershipService(lobbyState);
        playerRegistry = new LobbyPlayerRegistry(lobbyState, ownershipService);
        playerCustomizationService = new LobbyPlayerCustomizationService(lobbyState);
        settingsService = new LobbySettingsService(lobbyState);
        startService = new LobbyStartService(lobbyState, startRules, sessionService);
    }

    private void HandleClientConnected(ulong clientId)
    {
        playerRegistry.AddPlayerIfNotExists(clientId);
        startService.RefreshCanStartGame();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        playerRegistry.RemovePlayer(clientId);
        startService.RefreshCanStartGame();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSetReadyRpc(bool isReady, RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        playerCustomizationService.SetReady(senderClientId, isReady);
        startService.RefreshCanStartGame();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSetCharacterRpc(int characterId, RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        playerCustomizationService.SetCharacter(senderClientId, characterId);
        startService.RefreshCanStartGame();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSetGameModeRpc(int gameModeId, RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!ownershipService.CanChangeSettings(senderClientId))
            return;

        settingsService.SetGameMode(gameModeId);
        startService.RefreshCanStartGame();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSetMapRpc(int mapId, RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!ownershipService.CanChangeSettings(senderClientId))
            return;

        settingsService.SetMap(mapId);
        startService.RefreshCanStartGame();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestStartGameRpc(RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!ownershipService.CanStartGame(senderClientId))
            return;

        startService.TryStartGame();
    }

    public bool CanStartGame()
    {
        if (!IsServer)
            return false;

        return startService.CanStartGame();
    }
}