using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkPlayerSpawner : MonoBehaviour, IProjectSceneFlowServerActionHandler
{
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private NetworkPlayerOwnershipService ownershipService;
    [SerializeField] private GameMapService gameMapService;

    [Header("Player")]
    [SerializeField] private GameObject playerPrefab;

    private NetworkObject playerPrefabNetworkObject;

    private void Awake()
    {
        CachePlayerPrefabNetworkObject();

        if (!TryValidateSetup(out string error))
            Debug.LogError(error, this);
    }

    public bool CanHandle(ProjectSceneServerAction action)
    {
        return action == ProjectSceneServerAction.SpawnPlayers;
    }

    public ProjectSceneActionResult Validate(
        ProjectSceneServerAction action,
        ProjectSceneKind loadedScene)
    {
        if (!CanHandle(action))
        {
            return ProjectSceneActionResult.Failure(
                $"{nameof(NetworkPlayerSpawner)} cannot handle action '{action}'.");
        }

        if (loadedScene != ProjectSceneKind.Game)
        {
            return ProjectSceneActionResult.Failure(
                $"Action '{action}' is valid only for the Game scene, not '{loadedScene}'.");
        }

        CachePlayerPrefabNetworkObject();
        return TryValidateSetup(out string error)
            ? ProjectSceneActionResult.Success()
            : ProjectSceneActionResult.Failure(error);
    }

    public ProjectSceneActionResult Execute(
        ProjectSceneServerAction action,
        ProjectSceneKind loadedScene)
    {
        ProjectSceneActionResult validation = Validate(action, loadedScene);

        if (!validation.Succeeded)
            return validation;

        if (!networkManager.IsServer)
        {
            return ProjectSceneActionResult.Failure(
                "Only the server can spawn player objects.");
        }

        if (!NetworkObjectServiceContext.TryResolveSessionService(
                networkManager,
                out IPlayerScopeRegistry playerScopes))
        {
            return ProjectSceneActionResult.Failure(
                $"Cannot spawn players without {nameof(IPlayerScopeRegistry)}.");
        }

        List<NetworkObject> spawnedPlayers = new();
        Action rollback = () => RollbackSpawnedPlayers(spawnedPlayers);
        List<ulong> connectedClientIds = new(networkManager.ConnectedClientsIds);

        try
        {
            for (int i = 0; i < connectedClientIds.Count; i++)
            {
                ulong clientId = connectedClientIds[i];

                if (!TryEnsurePlayerForClient(
                        clientId,
                        playerScopes,
                        spawnedPlayers,
                        out string error))
                {
                    return ProjectSceneActionResult.Failure(error, rollback: rollback);
                }
            }

            return ProjectSceneActionResult.Success(rollback);
        }
        catch (Exception exception)
        {
            return ProjectSceneActionResult.Failure(
                "Player spawning threw an unexpected exception.",
                exception,
                rollback);
        }
    }

    private bool TryEnsurePlayerForClient(
        ulong clientId,
        IPlayerScopeRegistry playerScopes,
        ICollection<NetworkObject> spawnedPlayers,
        out string error)
    {
        if (!networkManager.ConnectedClients.TryGetValue(
                clientId,
                out NetworkClient client))
        {
            error = $"Cannot spawn player for disconnected client '{clientId}'.";
            return false;
        }

        if (client.PlayerObject != null)
        {
            return TryValidatePlayerScope(
                clientId,
                client.PlayerObject,
                playerScopes,
                out error);
        }

        Vector3 spawnPosition = playerPrefab.transform.position;
        Quaternion spawnRotation = playerPrefab.transform.rotation;

        if (!gameMapService.TryGetPlayerSpawn(
                clientId,
                out spawnPosition,
                out spawnRotation))
        {
            Debug.LogWarning(
                $"Map '{gameMapService.ActiveMap?.DisplayName}' has no valid spawn point for client {clientId}. " +
                "Using the player prefab transform.",
                gameMapService);
        }

        NetworkObject playerInstance = Instantiate(
            playerPrefabNetworkObject,
            spawnPosition,
            spawnRotation);
        spawnedPlayers.Add(playerInstance);

        if (!ownershipService.TrySpawnAsPlayerObject(playerInstance, clientId))
        {
            error = $"Failed to spawn player object for client '{clientId}'.";
            return false;
        }

        return TryValidatePlayerScope(
            clientId,
            playerInstance,
            playerScopes,
            out error);
    }

    private static bool TryValidatePlayerScope(
        ulong clientId,
        NetworkObject playerObject,
        IPlayerScopeRegistry playerScopes,
        out string error)
    {
        if (playerObject != null &&
            playerObject.IsSpawned &&
            playerScopes.TryGetPlayerScope(
                playerObject.NetworkObjectId,
                out IPlayerScope playerScope) &&
            playerScope != null &&
            !playerScope.IsDisposed)
        {
            error = string.Empty;
            return true;
        }

        error =
            $"Player object for client '{clientId}' spawned without a ready Player scope.";
        return false;
    }

    private void RollbackSpawnedPlayers(IReadOnlyList<NetworkObject> spawnedPlayers)
    {
        for (int i = spawnedPlayers.Count - 1; i >= 0; i--)
        {
            NetworkObject player = spawnedPlayers[i];

            if (player == null)
                continue;

            if (player.IsSpawned && networkManager != null && networkManager.IsServer)
            {
                player.Despawn(true);
                continue;
            }

            Destroy(player.gameObject);
        }
    }

    private void CachePlayerPrefabNetworkObject()
    {
        playerPrefabNetworkObject = null;

        if (playerPrefab != null)
            playerPrefab.TryGetComponent(out playerPrefabNetworkObject);
    }

    private bool TryValidateSetup(out string error)
    {
        if (networkManager == null)
        {
            error = $"{nameof(NetworkPlayerSpawner)} is missing '{nameof(networkManager)}'.";
            return false;
        }

        if (ownershipService == null)
        {
            error = $"{nameof(NetworkPlayerSpawner)} is missing '{nameof(ownershipService)}'.";
            return false;
        }

        if (gameMapService == null)
        {
            error = $"{nameof(NetworkPlayerSpawner)} is missing '{nameof(gameMapService)}'.";
            return false;
        }

        if (playerPrefab == null)
        {
            error = $"{nameof(NetworkPlayerSpawner)} is missing '{nameof(playerPrefab)}'.";
            return false;
        }

        if (playerPrefabNetworkObject == null)
        {
            error = $"Player prefab is missing {nameof(NetworkObject)}.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
