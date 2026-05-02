using Unity.Netcode;
using UnityEngine;

public class LobbyController : NetworkBehaviour
{
    [SerializeField] private LobbyState lobbyState;
    [SerializeField] private LobbyConfig lobbyConfig;

    private INetworkSessionService sessionService;

    private LobbyOwnershipService ownershipService;
    private LobbyPlayerRegistry playerRegistry;
    private LobbyPlayerCustomizationService playerCustomizationService;
    private LobbySettingsService settingsService;
    private LobbyStartService startService;

    private void Awake()
    {
        ResolveReferences();
    }

    public void Construct(INetworkSessionService sessionService)
    {
        if (sessionService == null)
        {
            Debug.LogError("Network session service is missing.");
            return;
        }

        this.sessionService = sessionService;
        CreateServices();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        if (!IsConstructed()) return;

        settingsService.InitializeFromConfig();

        NetworkManager.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

        playerRegistry.TryAddPlayer(NetworkManager.LocalClientId);
        startService.RefreshCanStartGame();
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager == null || !IsServer) return;

        NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    private void ResolveReferences()
    {
        if (lobbyState == null)
            lobbyState = GetComponent<LobbyState>();

        if (lobbyConfig == null)
            Debug.LogError("LobbyConfig is not assigned.");
    }

    private void CreateServices()
    {
        LobbyStartRules startRules = new LobbyStartRules();

        ownershipService = new LobbyOwnershipService(lobbyState);
        playerRegistry = new LobbyPlayerRegistry(lobbyState, ownershipService, lobbyConfig);
        playerCustomizationService = new LobbyPlayerCustomizationService(lobbyState, lobbyConfig);
        settingsService = new LobbySettingsService(lobbyState, lobbyConfig);
        startService = new LobbyStartService(lobbyState, startRules, sessionService);
    }

    private bool IsConstructed()
    {
        if (ownershipService != null &&
            playerRegistry != null &&
            playerCustomizationService != null &&
            settingsService != null &&
            startService != null)
        {
            return true;
        }

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
    public void RequestSetCharacterRpc(int characterId, RpcParams rpcParams = default)
    {
        if (!IsConstructed()) return;
        if (lobbyState.Phase.Value != LobbyPhase.Open) return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;

        playerCustomizationService.SetCharacter(senderClientId, characterId);
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
}
