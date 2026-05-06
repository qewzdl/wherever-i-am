using Unity.Netcode;
using UnityEngine;

public class NetworkChatSessionSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private GameStateMachine stateMachine;
    [SerializeField] private NetworkChatSession chatSessionPrefab;
    [SerializeField] private ScriptableObject chatConfig;

    private NetworkChatSession spawnedSession;

    private void Awake()
    {
        ResolveReferences();
    }

    public void SpawnForServer()
    {
        ResolveReferences();

        if (networkManager == null)
        {
            Debug.LogError("NetworkManager is missing.");
            return;
        }

        if (!networkManager.IsServer)
            return;

        if (chatSessionPrefab == null)
        {
            Debug.LogError("NetworkChatSession prefab is not assigned.");
            return;
        }

        if (spawnedSession != null)
        {
            if (spawnedSession.IsSpawned)
                return;

            Destroy(spawnedSession.gameObject);
            spawnedSession = null;
        }

        IChatConfig config = chatConfig as IChatConfig;

        if (chatConfig != null && config == null)
            Debug.LogWarning("Assigned chat config does not implement IChatConfig.");

        NetworkChatSession instance = Instantiate(chatSessionPrefab);
        instance.Construct(stateMachine, config);

        if (!instance.TryGetComponent(out NetworkObject networkObject))
        {
            Debug.LogError("NetworkChatSession prefab is missing NetworkObject.");
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

        ResolveReferences();

        if (networkManager == null || !networkManager.IsServer)
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

    private void ResolveReferences()
    {
        if (networkManager == null)
            networkManager = GetComponent<NetworkManager>();

        if (networkManager == null)
            networkManager = NetworkManager.Singleton;

        if (stateMachine == null)
            stateMachine = GetComponent<GameStateMachine>();
    }
}
