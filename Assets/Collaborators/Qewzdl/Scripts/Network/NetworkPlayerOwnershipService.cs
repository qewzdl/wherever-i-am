using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkPlayerOwnershipService : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;

    private void Awake()
    {
        HasRequiredReferences();
    }

    public bool CanSpawnPlayerObjectFor(ulong clientId)
    {
        if (!HasRequiredReferences())
            return false;

        if (!networkManager.IsServer)
        {
            Debug.LogWarning("Only server can assign player ownership.", this);
            return false;
        }

        if (!networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
        {
            Debug.LogWarning($"Cannot assign player ownership. Client '{clientId}' is not connected.", this);
            return false;
        }

        if (client.PlayerObject != null)
            return false;

        return true;
    }

    public bool TrySpawnAsPlayerObject(NetworkObject playerObject, ulong clientId)
    {
        if (playerObject == null)
        {
            Debug.LogError($"{nameof(NetworkPlayerOwnershipService)} received null player object.", this);
            return false;
        }

        if (playerObject.IsSpawned)
        {
            Debug.LogError("Player object is already spawned.", playerObject);
            return false;
        }

        if (!CanSpawnPlayerObjectFor(clientId))
            return false;

        playerObject.SpawnAsPlayerObject(clientId, true);
        return true;
    }

    private bool HasRequiredReferences()
    {
        return ValidateRequiredReference(networkManager, nameof(networkManager));
    }

    private bool ValidateRequiredReference(Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError($"{nameof(NetworkPlayerOwnershipService)} is missing '{fieldName}'.", this);
        return false;
    }
}