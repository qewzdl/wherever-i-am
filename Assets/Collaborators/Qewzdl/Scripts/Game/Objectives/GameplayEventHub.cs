using System;
using Unity.Netcode;
using UnityEngine;

public sealed class GameplayEventHub : NetworkBehaviour
{
    public event Action<GameplayEventData> GameplayEventRaised;

    public bool TryRaiseServerEvent(string eventId, ulong actorClientId = 0, NetworkObject sourceObject = null)
    {
        if (!IsSpawned || !IsServer)
        {
            Debug.LogError($"{nameof(GameplayEventHub)} can raise gameplay events only on server.", this);
            return false;
        }

        if (string.IsNullOrWhiteSpace(eventId))
        {
            Debug.LogError($"{nameof(GameplayEventHub)} received empty event id.", this);
            return false;
        }

        GameplayEventRaised?.Invoke(new GameplayEventData(eventId, actorClientId, sourceObject));
        return true;
    }
}