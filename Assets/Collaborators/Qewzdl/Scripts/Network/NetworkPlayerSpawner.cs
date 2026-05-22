using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkPlayerSpawner : MonoBehaviour, IProjectSceneFlowServerActionHandler
{
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;

    [Header("Player")]
    [SerializeField] private GameObject playerPrefab;

    private void Awake()
    {
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
        if (!networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
            return;

        if (client.PlayerObject != null)
            return;

        if (!playerPrefab.TryGetComponent(out NetworkObject playerNetworkObject))
        {
            Debug.LogError("Player prefab is missing NetworkObject.", playerPrefab);
            return;
        }

        NetworkObject playerInstance = Instantiate(
            playerNetworkObject,
            playerPrefab.transform.position,
            playerPrefab.transform.rotation);

        playerInstance.SpawnAsPlayerObject(clientId, true);
    }

    private bool CanSpawn()
    {
        if (!HasRequiredReferences())
            return false;

        return networkManager.IsServer;
    }

    private bool HasRequiredReferences()
    {
        if (networkManager == null)
        {
            Debug.LogError($"{nameof(NetworkPlayerSpawner)} is missing {nameof(NetworkManager)}.", this);
            return false;
        }

        if (playerPrefab == null)
        {
            Debug.LogError($"{nameof(NetworkPlayerSpawner)} is missing player prefab.", this);
            return false;
        }

        return true;
    }
}