using System;
using Unity.Netcode;
using UnityEngine;

public class LobbyState : NetworkBehaviour
{
    public NetworkList<LobbyPlayerData> Players { get; private set; }

    public event Action PlayersChanged;

    private void Awake()
    {
        Players = new NetworkList<LobbyPlayerData>();
    }

    public override void OnNetworkSpawn()
    {
        Players.OnListChanged += HandlePlayersChanged;
        PlayersChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        if (Players != null) Players.OnListChanged -= HandlePlayersChanged;
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