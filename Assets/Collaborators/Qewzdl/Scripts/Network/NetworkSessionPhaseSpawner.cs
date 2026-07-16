using System;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkSessionPhaseSpawner : MonoBehaviour,
    IProjectSceneFlowServerActionHandler
{
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private NetworkSessionPhaseService phaseServicePrefab;

    private NetworkSessionPhaseService spawnedService;

    public bool CanHandle(ProjectSceneServerAction action)
    {
        return action == ProjectSceneServerAction.SpawnSessionPhase;
    }

    public ProjectSceneActionResult Validate(
        ProjectSceneServerAction action,
        ProjectSceneKind loadedScene)
    {
        if (!CanHandle(action))
        {
            return ProjectSceneActionResult.Failure(
                $"{nameof(NetworkSessionPhaseSpawner)} cannot handle action '{action}'.");
        }

        if (loadedScene != ProjectSceneKind.Lobby)
        {
            return ProjectSceneActionResult.Failure(
                $"Action '{action}' is valid only for the Lobby scene, not '{loadedScene}'.");
        }

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
                "Only the server can spawn the authoritative Session phase service.");
        }

        if (spawnedService != null && spawnedService.IsSpawned)
            return ProjectSceneActionResult.Success();

        if (spawnedService != null)
        {
            Destroy(spawnedService.gameObject);
            spawnedService = null;
        }

        NetworkSessionPhaseService instance = null;

        try
        {
            instance = Instantiate(phaseServicePrefab);
            NetworkObject networkObject = instance.NetworkObject;
            spawnedService = instance;
            networkObject.Spawn(false);

            Action rollback = () => RollbackSpawn(instance);

            if (!networkObject.IsSpawned ||
                !NetworkObjectServiceContext.TryResolveSessionService(
                    networkManager,
                    out ISessionPhaseService phaseService) ||
                !ReferenceEquals(phaseService, instance))
            {
                return ProjectSceneActionResult.Failure(
                    $"Spawned {nameof(NetworkSessionPhaseService)} did not publish " +
                    $"{nameof(ISessionPhaseService)} in the active Session scope.",
                    rollback: rollback);
            }

            return ProjectSceneActionResult.Success(rollback);
        }
        catch (Exception exception)
        {
            RollbackSpawn(instance);
            return ProjectSceneActionResult.Failure(
                $"Failed to spawn {nameof(NetworkSessionPhaseService)}.",
                exception);
        }
    }

    private void RollbackSpawn(NetworkSessionPhaseService instance)
    {
        if (instance == null)
            return;

        if (spawnedService == instance)
            spawnedService = null;

        NetworkObject networkObject = instance.NetworkObject;

        if (networkObject != null && networkObject.IsSpawned &&
            networkManager != null && networkManager.IsServer)
        {
            networkObject.Despawn(true);
            return;
        }

        Destroy(instance.gameObject);
    }

    private bool TryValidateSetup(out string error)
    {
        if (networkManager == null)
        {
            error = $"{nameof(NetworkSessionPhaseSpawner)} is missing '{nameof(networkManager)}'.";
            return false;
        }

        if (phaseServicePrefab == null)
        {
            error = $"{nameof(NetworkSessionPhaseSpawner)} is missing '{nameof(phaseServicePrefab)}'.";
            return false;
        }

        if (!phaseServicePrefab.TryGetComponent(out NetworkObject _))
        {
            error = $"{nameof(NetworkSessionPhaseService)} prefab is missing {nameof(NetworkObject)}.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
