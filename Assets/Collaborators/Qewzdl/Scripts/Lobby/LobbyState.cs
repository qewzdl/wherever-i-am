using System;
using Unity.Netcode;
using UnityEngine;

public class LobbyState : NetworkBehaviour
{
    public const ulong NoRoomOwner = ulong.MaxValue;

    public NetworkList<LobbyPlayerData> Players { get; private set; }

    public NetworkVariable<ulong> RoomOwnerClientId { get; } = new NetworkVariable<ulong>(
        NoRoomOwner,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> CanStartGame { get; } = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public event Action PlayersChanged;

    private void Awake()
    {
        Players = new NetworkList<LobbyPlayerData>();
    }

    public override void OnNetworkSpawn()
    {
        Players.OnListChanged += HandlePlayersChanged;
        RoomOwnerClientId.OnValueChanged += HandleRoomOwnerChanged;
        CanStartGame.OnValueChanged += HandleCanStartGameChanged;

        PlayersChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        if (Players != null)
            Players.OnListChanged -= HandlePlayersChanged;

        RoomOwnerClientId.OnValueChanged -= HandleRoomOwnerChanged;
        CanStartGame.OnValueChanged -= HandleCanStartGameChanged;
    }

    public override void OnDestroy()
    {
        Players?.Dispose();
        base.OnDestroy();
    }

    private void HandlePlayersChanged(NetworkListEvent<LobbyPlayerData> changeEvent)
    {
        PlayersChanged?.Invoke();
    }

    private void HandleRoomOwnerChanged(ulong previousOwnerId, ulong newOwnerId)
    {
        PlayersChanged?.Invoke();
    }

    private void HandleCanStartGameChanged(bool previousValue, bool newValue)
    {
        PlayersChanged?.Invoke();
    }

    public bool TryGetPlayerIndex(ulong clientId, out int index)
    {
        for (int i = 0; i < Players.Count; i++)
        {
            if (Players[i].ClientId == clientId)
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }
}