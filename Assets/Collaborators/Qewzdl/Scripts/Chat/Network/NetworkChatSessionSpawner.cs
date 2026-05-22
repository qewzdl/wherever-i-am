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
        HasRequiredReferences();
    }

    public bool CanHandle(ProjectSceneServerAction action)
    {
        return action == ProjectSceneServerAction.SpawnChatSession;
    }

    public void Handle(ProjectSceneServerAction action, ProjectSceneKind loadedScene)
    {
        if (!CanHandle(action))
            return;

        if (loadedScene != ProjectSceneKind.Lobby)
        {
            Debug.LogWarning(
                $"{nameof(NetworkChatSessionSpawner)} received '{action}' for non-lobby scene '{loadedScene}'.",
                this);
            return;
        }

        SpawnForServer();
    }

    public void SpawnForServer()
    {
        if (!HasRequiredReferences())
            return;

        if (!networkManager.IsServer)
            return;

        if (spawnedSession != null)
        {
            if (spawnedSession.IsSpawned)
                return;

            Destroy(spawnedSession.gameObject);
            spawnedSession = null;
        }

        NetworkChatSession instance = Instantiate(chatSessionPrefab);
        instance.Construct(stateMachine, chatConfig);

        if (!instance.TryGetComponent(out NetworkObject networkObject))
        {
            Debug.LogError("NetworkChatSession prefab is missing NetworkObject.", instance);
            Destroy(instance.gameObject);
            return;
        }

        spawnedSession = instance;
        networkObject.Spawn(false);
    }

    public void DespawnForServer()
    {
        if (spawnedSession == null)
            return;

        if (!HasRequiredReferences())
            return;

        if (!networkManager.IsServer)
            return;

        if (spawnedSession.IsSpawned)
        {
            spawnedSession.NetworkObject.Despawn(true);
            spawnedSession = null;
            return;
        }

        Destroy(spawnedSession.gameObject);
        spawnedSession = null;
    }

    private bool HasRequiredReferences()
    {
        if (networkManager == null)
        {
            Debug.LogError($"{nameof(NetworkChatSessionSpawner)} is missing {nameof(NetworkManager)}.", this);
            return false;
        }

        if (stateMachine == null)
        {
            Debug.LogError($"{nameof(NetworkChatSessionSpawner)} is missing {nameof(GameStateMachine)}.", this);
            return false;
        }

        if (chatSessionPrefab == null)
        {
            Debug.LogError($"{nameof(NetworkChatSessionSpawner)} is missing {nameof(NetworkChatSession)} prefab.", this);
            return false;
        }

        if (chatConfig == null)
        {
            Debug.LogError($"{nameof(NetworkChatSessionSpawner)} is missing {nameof(ChatConfig)}.", this);
            return false;
        }

        return true;
    }
}