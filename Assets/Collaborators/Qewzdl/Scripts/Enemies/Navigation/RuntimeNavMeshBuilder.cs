using System.Collections;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshSurface))]
public class RuntimeNavMeshBuilder : MonoBehaviour
{
    [Header("Build")]
    [SerializeField] private RuntimeNavMeshBuildMode buildMode = RuntimeNavMeshBuildMode.ServerOnly;
    [SerializeField] private float serverWaitTimeout = 5f;
    [SerializeField] private bool waitForGameMap = true;
    [SerializeField] private bool buildOverMultipleFrames = true;

    [Header("Surface")]
    [SerializeField] private NavMeshSurface surface;
    [SerializeField] private LayerMask includedLayers = Physics.DefaultRaycastLayers;
    [SerializeField] private NavMeshCollectGeometry geometry = NavMeshCollectGeometry.PhysicsColliders;

    private NavMeshSurface[] surfaces;
    private bool hasBuilt;
    private Coroutine buildWhenServerReadyCoroutine;
    private Coroutine buildCoroutine;
    private IGameMapSessionService gameMapService;
    private NetworkManager networkManager;
    private bool subscribedToGameMap;

    public bool HasBuilt => hasBuilt;
    public NavMeshSurface Surface => surface;

    public event System.Action<RuntimeNavMeshBuilder> Built;

    private void Awake()
    {
        CacheSurface();
    }

    private void Start()
    {

        if (buildMode == RuntimeNavMeshBuildMode.Disabled)
        {
            return;
        }

        if (ShouldWaitForGameMap())
        {
            SubscribeToGameMap();
            return;
        }

        StartBuildFlow();
    }

    public void Construct(
        IGameMapSessionService mapService,
        NetworkManager runtimeNetworkManager)
    {
        gameMapService = mapService;
        networkManager = runtimeNetworkManager;
    }

    private void StartBuildFlow()
    {
        if (ShouldBuildImmediately())
        {
            StartAllowedBuild();
            return;
        }

        if (buildMode == RuntimeNavMeshBuildMode.ServerOnly)
        {
            StartBuildWhenServerIsReady();
        }
    }

    private bool ShouldWaitForGameMap()
    {
        if (!waitForGameMap)
            return false;

        return gameMapService != null && !gameMapService.IsReadyForMatch;
    }

    private void SubscribeToGameMap()
    {
        if (subscribedToGameMap || gameMapService == null)
            return;

        gameMapService.MapReady += HandleGameMapReady;
        subscribedToGameMap = true;
    }

    private void UnsubscribeFromGameMap()
    {
        if (!subscribedToGameMap || gameMapService == null)
            return;

        gameMapService.MapReady -= HandleGameMapReady;
        subscribedToGameMap = false;
    }

    private void HandleGameMapReady()
    {
        UnsubscribeFromGameMap();
        StartBuildFlow();
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

        return StartAllowedBuild();
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
                StartAllowedBuild();
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
        return networkManager != null &&
               networkManager.IsListening &&
               networkManager.IsServer;
    }

    private bool BuildNavMesh()
    {
        if (hasBuilt)
        {
            return true;
        }

        if (!TryGetSurfaces(out NavMeshSurface[] navMeshSurfaces))
        {
            return false;
        }

        foreach (NavMeshSurface navMeshSurface in navMeshSurfaces)
        {
            ConfigureSurface(navMeshSurface);
            navMeshSurface.BuildNavMesh();
        }

        hasBuilt = true;
        Built?.Invoke(this);

        return true;
    }

    private bool StartAllowedBuild()
    {
        if (hasBuilt)
        {
            return true;
        }

        if (!buildOverMultipleFrames)
        {
            return BuildNavMesh();
        }

        if (buildCoroutine == null)
        {
            buildCoroutine = StartCoroutine(BuildNavMeshOverMultipleFrames());
        }

        return false;
    }

    private IEnumerator BuildNavMeshOverMultipleFrames()
    {
        if (!TryGetSurfaces(out NavMeshSurface[] navMeshSurfaces))
        {
            buildCoroutine = null;
            yield break;
        }

        for (int i = 0; i < navMeshSurfaces.Length; i++)
        {
            NavMeshSurface navMeshSurface = navMeshSurfaces[i];
            ConfigureSurface(navMeshSurface);

            if (navMeshSurface.navMeshData != null)
            {
                AsyncOperation updateOperation =
                    navMeshSurface.UpdateNavMesh(navMeshSurface.navMeshData);
                yield return updateOperation;
            }
            else
            {
                navMeshSurface.BuildNavMesh();
            }

            if (i + 1 < navMeshSurfaces.Length)
            {
                yield return null;
            }
        }

        hasBuilt = true;
        buildCoroutine = null;
        Built?.Invoke(this);
    }

    private void ConfigureSurface(NavMeshSurface navMeshSurface)
    {
        navMeshSurface.collectObjects = CollectObjects.All;
        navMeshSurface.layerMask = includedLayers;
        navMeshSurface.useGeometry = geometry;
        navMeshSurface.ignoreNavMeshAgent = true;
        navMeshSurface.ignoreNavMeshObstacle = true;
    }

    private bool TryGetSurfaces(out NavMeshSurface[] navMeshSurfaces)
    {
        CacheSurface();

        navMeshSurfaces = surfaces;

        if (navMeshSurfaces != null && navMeshSurfaces.Length > 0)
        {
            return true;
        }

        Debug.LogError(
            $"{nameof(RuntimeNavMeshBuilder)} requires at least one {nameof(NavMeshSurface)}.",
            this
        );

        return false;
    }

    private void CacheSurface()
    {
        NavMeshSurface[] localSurfaces = GetComponents<NavMeshSurface>();

        if (surface == null && localSurfaces.Length > 0)
        {
            surface = localSurfaces[0];
        }

        if (surface == null || ContainsSurface(localSurfaces, surface))
        {
            surfaces = localSurfaces;
            return;
        }

        surfaces = new NavMeshSurface[localSurfaces.Length + 1];
        surfaces[0] = surface;

        for (int i = 0; i < localSurfaces.Length; i++)
        {
            surfaces[i + 1] = localSurfaces[i];
        }
    }

    private static bool ContainsSurface(NavMeshSurface[] navMeshSurfaces, NavMeshSurface target)
    {
        for (int i = 0; i < navMeshSurfaces.Length; i++)
        {
            if (navMeshSurfaces[i] == target)
            {
                return true;
            }
        }

        return false;
    }

    private void OnDisable()
    {
        UnsubscribeFromGameMap();

        if (buildWhenServerReadyCoroutine != null)
        {
            StopCoroutine(buildWhenServerReadyCoroutine);
            buildWhenServerReadyCoroutine = null;
        }

        if (buildCoroutine != null)
        {
            StopCoroutine(buildCoroutine);
            buildCoroutine = null;
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheSurface();
    }

    private void OnValidate()
    {
        serverWaitTimeout = Mathf.Max(0f, serverWaitTimeout);
        CacheSurface();
    }

    [ContextMenu("Build NavMesh Now")]
    private void BuildNavMeshNow()
    {
        hasBuilt = false;
        BuildNavMesh();
    }
#endif
}
