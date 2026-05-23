using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkPlayerSpawner : MonoBehaviour, IProjectSceneFlowServerActionHandler
{
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private NetworkPlayerOwnershipService ownershipService;

    [Header("Player")]
    [SerializeField] private GameObject playerPrefab;

    private NetworkObject playerPrefabNetworkObject;

    private void Awake()
    {
        CachePlayerPrefabNetworkObject();
        HasRequiredReferences();
    }

    public bool CanHandle(ProjectSceneServerAction action)
    {
        return action == ProjectSceneServerAction.SpawnPlayers;
    }

    public void Handle(ProjectSceneServerAction action, ProjectSceneKind loadedScene)
    {
        if (!CanHandle(action))
            return;

        if (loadedScene != ProjectSceneKind.Game)
        {
            Debug.LogWarning(
                $"{nameof(NetworkPlayerSpawner)} received '{action}' for non-game scene '{loadedScene}'.",
                this);

            return;
        }

        SpawnPlayersForConnectedClients();
    }

    private void SpawnPlayersForConnectedClients()
    {
        if (!CanSpawn())
            return;

        foreach (ulong clientId in networkManager.ConnectedClientsIds)
            SpawnPlayerForClient(clientId);
    }

    private void SpawnPlayerForClient(ulong clientId)
    {
        if (!ownershipService.CanSpawnPlayerObjectFor(clientId))
            return;

        NetworkObject playerInstance = Instantiate(
            playerPrefabNetworkObject,
            playerPrefab.transform.position,
            playerPrefab.transform.rotation);

        if (ownershipService.TrySpawnAsPlayerObject(playerInstance, clientId))
            return;

        Destroy(playerInstance.gameObject);
    }

    private bool CanSpawn()
    {
        if (!HasRequiredReferences())
            return false;

        if (networkManager.IsServer)
            return true;

        Debug.LogWarning("Only server can spawn player objects.", this);
        return false;
    }

    private void CachePlayerPrefabNetworkObject()
    {
        playerPrefabNetworkObject = null;

        if (playerPrefab == null)
            return;

        playerPrefab.TryGetComponent(out playerPrefabNetworkObject);
    }

    private bool HasRequiredReferences()
    {
        bool valid = true;

        valid &= ValidateRequiredReference(networkManager, nameof(networkManager));
        valid &= ValidateRequiredReference(ownershipService, nameof(ownershipService));
        valid &= ValidateRequiredReference(playerPrefab, nameof(playerPrefab));

        if (playerPrefab != null && playerPrefabNetworkObject == null)
        {
            Debug.LogError("Player prefab is missing NetworkObject.", playerPrefab);
            valid = false;
        }

        return valid;
    }

    private bool ValidateRequiredReference(Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError($"{nameof(NetworkPlayerSpawner)} is missing '{fieldName}'.", this);
        return false;
    }
}