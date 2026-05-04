using System.Collections;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public class RuntimeNavMeshBuilder : MonoBehaviour
{
    [Header("Build")]
    [SerializeField] private RuntimeNavMeshBuildMode buildMode = RuntimeNavMeshBuildMode.ServerOnly;
    [SerializeField] private float serverWaitTimeout = 5f;

    [Header("Surface")]
    [SerializeField] private LayerMask includedLayers = Physics.DefaultRaycastLayers;
    [SerializeField] private NavMeshCollectGeometry geometry = NavMeshCollectGeometry.PhysicsColliders;

    private NavMeshSurface surface;
    private bool hasBuilt;
    private Coroutine buildWhenServerReadyCoroutine;

    public bool HasBuilt => hasBuilt;
    public NavMeshSurface Surface => surface;

    public event System.Action<RuntimeNavMeshBuilder> Built;

    private void Awake()
    {
        if (buildMode == RuntimeNavMeshBuildMode.Disabled)
        {
            return;
        }

        if (ShouldBuildImmediately())
        {
            BuildNavMesh();
            return;
        }

        if (buildMode == RuntimeNavMeshBuildMode.ServerOnly)
        {
            StartBuildWhenServerIsReady();
        }
    }

    public bool BuildIfAllowed()
    {
        if (hasBuilt)
        {
            return true;
        }

        if (buildMode == RuntimeNavMeshBuildMode.Disabled)
        {
            return false;
        }

        if (!ShouldBuildImmediately())
        {
            return false;
        }

        BuildNavMesh();
        return hasBuilt;
    }

    public void AddBuiltListener(System.Action<RuntimeNavMeshBuilder> listener, bool notifyImmediatelyIfBuilt = true)
    {
        if (listener == null)
        {
            return;
        }

        Built += listener;

        if (notifyImmediatelyIfBuilt && hasBuilt)
        {
            listener.Invoke(this);
        }
    }

    public void RemoveBuiltListener(System.Action<RuntimeNavMeshBuilder> listener)
    {
        if (listener == null)
        {
            return;
        }

        Built -= listener;
    }

    private void StartBuildWhenServerIsReady()
    {
        if (buildWhenServerReadyCoroutine != null)
        {
            return;
        }

        buildWhenServerReadyCoroutine = StartCoroutine(BuildWhenServerIsReady());
    }

    private bool ShouldBuildImmediately()
    {
        switch (buildMode)
        {
            case RuntimeNavMeshBuildMode.Always:
                return true;

            case RuntimeNavMeshBuildMode.EditorOnly:
#if UNITY_EDITOR
                return true;
#else
                return false;
#endif

            case RuntimeNavMeshBuildMode.ServerOnly:
                return IsServerReady();

            default:
                return false;
        }
    }

    private IEnumerator BuildWhenServerIsReady()
    {
        float deadline = Time.unscaledTime + serverWaitTimeout;

        while (Time.unscaledTime < deadline)
        {
            if (IsServerReady())
            {
                BuildNavMesh();
                buildWhenServerReadyCoroutine = null;
                yield break;
            }

            yield return null;
        }

        buildWhenServerReadyCoroutine = null;

        Debug.LogWarning(
            $"{nameof(RuntimeNavMeshBuilder)} did not build NavMesh because server was not ready within {serverWaitTimeout:0.##} seconds.",
            this
        );
    }

    private bool IsServerReady()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        return networkManager != null &&
               networkManager.IsListening &&
               networkManager.IsServer;
    }

    private void BuildNavMesh()
    {
        if (hasBuilt)
        {
            return;
        }

        surface = GetComponent<NavMeshSurface>();

        if (surface == null)
        {
            surface = gameObject.AddComponent<NavMeshSurface>();
        }

        surface.collectObjects = CollectObjects.All;
        surface.layerMask = includedLayers;
        surface.useGeometry = geometry;
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = true;
        surface.BuildNavMesh();

        hasBuilt = true;
        Built?.Invoke(this);
    }

#if UNITY_EDITOR
    [ContextMenu("Build NavMesh Now")]
    private void BuildNavMeshNow()
    {
        hasBuilt = false;
        BuildNavMesh();
    }
#endif
}