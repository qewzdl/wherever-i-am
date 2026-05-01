using Unity.Netcode;
using UnityEngine;

public class LobbyController : NetworkBehaviour
{
    [SerializeField] private LobbyState lobbyState;
    [SerializeField] private LobbyConfig lobbyConfig;

    private LobbyStartRules startRules;

    private void Awake()
    {
        if (lobbyState == null)
            lobbyState = GetComponent<LobbyState>();

        if (lobbyConfig == null)
            Debug.LogError("LobbyConfig is not assigned.");

        startRules = new LobbyStartRules();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        InitializeLobbySettings();

        NetworkManager.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

        AddPlayerIfNotExists(NetworkManager.LocalClientId);
        RefreshCanStartGame();
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager == null || !IsServer) return;

        NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    private void InitializeLobbySettings()
    {
        if (!HasLobbyState())
            return;

        lobbyState.Settings.Value = LobbySettingsData.FromConfig(lobbyConfig);
    }

    private void HandleClientConnected(ulong clientId)
    {
        AddPlayerIfNotExists(clientId);
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        RemovePlayer(clientId);
    }

    private void AddPlayerIfNotExists(ulong clientId)
    {
        if (!HasLobbyState()) return;

        if (lobbyState.TryGetPlayerIndex(clientId, out _)) return;

        bool shouldBecomeRoomOwner = !HasValidRoomOwner();

        if (shouldBecomeRoomOwner)
            lobbyState.RoomOwnerClientId.Value = clientId;

        lobbyState.Players.Add(new LobbyPlayerData(
            clientId,
            $"Player {clientId}",
            false,
            shouldBecomeRoomOwner,
            0
        ));

        RefreshCanStartGame();
    }

    private void RemovePlayer(ulong clientId)
    {
        if (!HasLobbyState()) return;

        if (!lobbyState.TryGetPlayerIndex(clientId, out int index)) return;

        bool wasRoomOwner = IsRoomOwner(clientId);

        lobbyState.Players.RemoveAt(index);

        if (wasRoomOwner)
            AssignNextRoomOwner();

        RefreshCanStartGame();
    }

    private void AssignNextRoomOwner()
    {
        if (!HasLobbyState()) return;

        if (lobbyState.Players.Count == 0)
        {
            lobbyState.RoomOwnerClientId.Value = LobbyState.NoRoomOwner;
            return;
        }

        ulong nextOwnerClientId = lobbyState.Players[0].ClientId;
        lobbyState.RoomOwnerClientId.Value = nextOwnerClientId;

        for (int i = 0; i < lobbyState.Players.Count; i++)
        {
            LobbyPlayerData player = lobbyState.Players[i];
            player.IsRoomOwner = player.ClientId == nextOwnerClientId;
            lobbyState.Players[i] = player;
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSetReadyRpc(bool isReady, RpcParams rpcParams = default)
    {
        if (!HasLobbyState()) return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!lobbyState.TryGetPlayerIndex(senderClientId, out int index)) return;

        LobbyPlayerData player = lobbyState.Players[index];
        player.IsReady = isReady;

        lobbyState.Players[index] = player;

        RefreshCanStartGame();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSetCharacterRpc(int characterId, RpcParams rpcParams = default)
    {
        if (!HasLobbyState()) return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!lobbyState.TryGetPlayerIndex(senderClientId, out int index)) return;

        LobbyPlayerData player = lobbyState.Players[index];
        player.CharacterId = characterId;

        lobbyState.Players[index] = player;

        RefreshCanStartGame();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSetGameModeRpc(int gameModeId, RpcParams rpcParams = default)
    {
        if (!CanSenderChangeSettings(rpcParams, out _))
            return;

        LobbySettingsData settings = lobbyState.Settings.Value;
        settings.GameModeId = gameModeId;
        lobbyState.Settings.Value = settings;

        RefreshCanStartGame();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSetMapRpc(int mapId, RpcParams rpcParams = default)
    {
        if (!CanSenderChangeSettings(rpcParams, out _))
            return;

        LobbySettingsData settings = lobbyState.Settings.Value;
        settings.MapId = mapId;
        lobbyState.Settings.Value = settings;

        RefreshCanStartGame();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestStartGameRpc(RpcParams rpcParams = default)
    {
        if (!HasLobbyState()) return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!IsRoomOwner(senderClientId))
        {
            Debug.LogWarning("Only room owner can request game start.");
            return;
        }

        TryStartGame();
    }

    private bool CanSenderChangeSettings(RpcParams rpcParams, out ulong senderClientId)
    {
        senderClientId = rpcParams.Receive.SenderClientId;

        if (!HasLobbyState())
            return false;

        if (!IsRoomOwner(senderClientId))
        {
            Debug.LogWarning("Only room owner can change lobby settings.");
            return false;
        }

        return true;
    }

    private bool HasLobbyState()
    {
        if (lobbyState != null)
            return true;

        Debug.LogError("LobbyState is missing.");
        return false;
    }

    private bool IsRoomOwner(ulong clientId)
    {
        return lobbyState != null && lobbyState.RoomOwnerClientId.Value == clientId;
    }

    private bool HasValidRoomOwner()
    {
        return lobbyState != null &&
               lobbyState.RoomOwnerClientId.Value != LobbyState.NoRoomOwner &&
               lobbyState.TryGetPlayerIndex(lobbyState.RoomOwnerClientId.Value, out _);
    }

    public bool CanStartGame()
    {
        if (!IsServer)
            return false;

        if (!HasLobbyState())
            return false;

        return lobbyState.CanStartGame.Value;
    }

    private void RefreshCanStartGame()
    {
        if (!IsServer)
            return;

        if (!HasLobbyState())
            return;

        lobbyState.CanStartGame.Value = startRules != null && startRules.CanStart(lobbyState);
    }

    private void TryStartGame()
    {
        RefreshCanStartGame();

        if (!CanStartGame()) return;

        if (NetworkSessionOrchestrator.Instance == null)
        {
            Debug.LogError("NetworkSessionOrchestrator.Instance is null.");
            return;
        }

        NetworkSessionOrchestrator.Instance.StartGame();
    }
}