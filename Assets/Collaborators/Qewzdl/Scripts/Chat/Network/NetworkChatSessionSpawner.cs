using System;
using Unity.Netcode;
using UnityEngine;

public class NetworkChatSessionSpawner : MonoBehaviour, IProjectSceneFlowServerActionHandler
{
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private GameStateMachine stateMachine;
    [SerializeField] private NetworkChatSession chatSessionPrefab;
    [SerializeField] private ChatConfig chatConfig;

    private NetworkChatSession spawnedSession;

    private void Awake()
    {
        if (!TryValidateSetup(out string error))
            Debug.LogError(error, this);
    }

    public bool CanHandle(ProjectSceneServerAction action)
    {
        return action == ProjectSceneServerAction.SpawnChatSession;
    }

    public ProjectSceneActionResult Validate(
        ProjectSceneServerAction action,
        ProjectSceneKind loadedScene)
    {
        if (!CanHandle(action))
        {
            return ProjectSceneActionResult.Failure(
                $"{nameof(NetworkChatSessionSpawner)} cannot handle action '{action}'.");
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
                "Only the server can spawn the network chat session.");
        }

        if (spawnedSession != null && spawnedSession.IsSpawned)
            return ProjectSceneActionResult.Success();

        if (spawnedSession != null)
        {
            Destroy(spawnedSession.gameObject);
            spawnedSession = null;
        }

        NetworkChatSession instance = null;

        try
        {
            instance = Instantiate(chatSessionPrefab);
            instance.Construct(stateMachine, chatConfig);

            if (!instance.TryGetComponent(out NetworkObject networkObject))
            {
                Destroy(instance.gameObject);
                return ProjectSceneActionResult.Failure(
                    $"{nameof(NetworkChatSession)} prefab is missing {nameof(NetworkObject)}.");
            }

            spawnedSession = instance;
            networkObject.Spawn(false);

            if (!networkObject.IsSpawned)
            {
                RollbackSpawn(instance);
                return ProjectSceneActionResult.Failure(
                    $"{nameof(NetworkChatSession)} did not enter the spawned state.");
            }

            Action rollback = () => RollbackSpawn(instance);

            if (!NetworkObjectServiceContext.TryResolveSessionService(
                    networkManager,
                    out IChatReadService readService) ||
                !ReferenceEquals(readService, instance) ||
                !NetworkObjectServiceContext.TryResolveSessionService(
                    networkManager,
                    out IChatCommandService commandService) ||
                !ReferenceEquals(commandService, instance))
            {
                return ProjectSceneActionResult.Failure(
                    $"Spawned {nameof(NetworkChatSession)} did not publish both chat " +
                    "contracts in the active Session scope.",
                    rollback: rollback);
            }

            return ProjectSceneActionResult.Success(
                rollback);
        }
        catch (Exception exception)
        {
            RollbackSpawn(instance);
            return ProjectSceneActionResult.Failure(
                $"Failed to spawn {nameof(NetworkChatSession)}.",
                exception);
        }
    }

    public void SpawnForServer()
    {
        ProjectSceneActionResult result = Execute(
            ProjectSceneServerAction.SpawnChatSession,
            ProjectSceneKind.Lobby);

        if (result.Succeeded)
        {
            result.Commit();
            return;
        }

        result.Rollback();
        Debug.LogError(result.Error, this);

        if (result.Exception != null)
            Debug.LogException(result.Exception, this);
    }

    public void DespawnForServer()
    {
        NetworkChatSession session = spawnedSession;

        if (session == null)
            return;

        if (networkManager == null || !networkManager.IsServer)
        {
            Debug.LogWarning("Only the server can despawn the network chat session.", this);
            return;
        }

        RollbackSpawn(session);
    }

    private void RollbackSpawn(NetworkChatSession instance)
    {
        if (instance == null)
            return;

        if (spawnedSession == instance)
            spawnedSession = null;

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
            error = $"{nameof(NetworkChatSessionSpawner)} is missing '{nameof(networkManager)}'.";
            return false;
        }

        if (stateMachine == null)
        {
            error = $"{nameof(NetworkChatSessionSpawner)} is missing '{nameof(stateMachine)}'.";
            return false;
        }

        if (chatSessionPrefab == null)
        {
            error = $"{nameof(NetworkChatSessionSpawner)} is missing '{nameof(chatSessionPrefab)}'.";
            return false;
        }

        if (!chatSessionPrefab.TryGetComponent(out NetworkObject _))
        {
            error = $"{nameof(NetworkChatSession)} prefab is missing {nameof(NetworkObject)}.";
            return false;
        }

        if (chatConfig == null)
        {
            error = $"{nameof(NetworkChatSessionSpawner)} is missing '{nameof(chatConfig)}'.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
