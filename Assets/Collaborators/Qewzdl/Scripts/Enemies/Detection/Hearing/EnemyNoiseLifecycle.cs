using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyNoiseLifecycle : MonoBehaviour
{
    [Header("Cleanup")]
    [SerializeField] private bool clearOnInitialize = true;
    [SerializeField] private bool clearOnDestroy = true;

    [Tooltip("Allows cleanup in editor/offline scene runs where NetworkManager is not listening yet.")]
    [SerializeField] private bool clearWithoutActiveNetworkSession = true;

    [Header("Debug")]
    [SerializeField] private bool logLifecycleCleanup;

    private bool initialized;

    public void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;

        if (clearOnInitialize)
        {
            ClearIfAllowed("initialize");
        }
    }

    private void Awake()
    {
        Initialize();
    }

    private void OnDestroy()
    {
        if (clearOnDestroy)
        {
            ClearIfAllowed("destroy");
        }

        initialized = false;
    }

    private void ClearIfAllowed(string reason)
    {
        if (!CanClearForCurrentContext())
        {
            return;
        }

        EnemyNoiseSystem.Clear();

        if (logLifecycleCleanup)
        {
            Debug.Log(
                $"{nameof(EnemyNoiseLifecycle)} cleared {nameof(EnemyNoiseSystem)} on {reason}.",
                this
            );
        }
    }

    private bool CanClearForCurrentContext()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null || !networkManager.IsListening)
        {
            return clearWithoutActiveNetworkSession;
        }

        return networkManager.IsServer;
    }
}