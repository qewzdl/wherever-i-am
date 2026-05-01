using Unity.Netcode;
using UnityEngine;

public class LobbyController : NetworkBehaviour
{
    [SerializeField] private LobbyState lobbyState;
    [SerializeField] private LobbyConfig lobbyConfig;

    private void Awake()
    {
        if (lobbyState == null)
            lobbyState = GetComponent<LobbyState>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        NetworkManager.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

        AddPlayerIfNotExists(NetworkManager.LocalClientId);
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager == null || !IsServer) return;

        NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
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
        if (lobbyState == null)
        {
            Debug.LogError("LobbyState is missing.");
            return;
        }

        if (lobbyState.TryGetPlayerIndex(clientId, out _)) return;

        bool isHost = clientId == NetworkManager.ServerClientId;

        lobbyState.Players.Add(new LobbyPlayerData(
            clientId,
            $"Player {clientId}",
            false,
            isHost,
            0
        ));
    }

    private void RemovePlayer(ulong clientId)
    {
        if (lobbyState == null) return;

        if (!lobbyState.TryGetPlayerIndex(clientId, out int index)) return;

        lobbyState.Players.RemoveAt(index);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSetReadyRpc(bool isReady, RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!lobbyState.TryGetPlayerIndex(senderClientId, out int index))
            return;

        LobbyPlayerData player = lobbyState.Players[index];
        player.IsReady = isReady;

        lobbyState.Players[index] = player;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSetCharacterRpc(int characterId, RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!lobbyState.TryGetPlayerIndex(senderClientId, out int index))
            return;

        LobbyPlayerData player = lobbyState.Players[index];
        player.CharacterId = characterId;

        lobbyState.Players[index] = player;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestStartGameRpc(RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (senderClientId != NetworkManager.ServerClientId)
        {
            Debug.LogWarning("Only host can request game start.");
            return;
        }

        TryStartGame();
    }

    public bool CanStartGame()
    {
        if (!IsServer) return false;

        if (lobbyConfig == null)
        {
            Debug.LogError("LobbyConfig is missing."); return false;
        }

        if (lobbyState.Players.Count < lobbyConfig.MinPlayersToStart) return false;

        if (lobbyState.Players.Count > lobbyConfig.MaxPlayers) return false;

        if (!lobbyConfig.RequireAllPlayersReady) return true;

        for (int i = 0; i < lobbyState.Players.Count; i++)
        {
            if (!lobbyState.Players[i].IsReady) return false;
        }

        return true;
    }

    private void TryStartGame()
    {
        if (!CanStartGame()) return;

        NetworkSessionOrchestrator.Instance.StartGame();
    }
}