using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class GameMapService : MonoBehaviour, IGameMapSessionService, IProjectSceneLoadCompletionGate
{
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private GameMapCatalog catalog;

    private IProjectSceneRegistry sceneRegistry;
    private GameMapDefinition selectedMap;
    private GameMapDefinition activeMap;
    private GameMapRoot activeMapRoot;
    private readonly HashSet<string> cancelledMapLoads = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase);
    private Action<bool> pendingCompletion;
    private bool localLoadRequested;
    private bool networkLoadSubscribed;
    private bool readyForMatch;
    private int operationVersion;

    public event Action MapReady;

    public GameMapCatalog Catalog => catalog;
    public GameMapDefinition SelectedMap => selectedMap;
    public GameMapDefinition ActiveMap => activeMap;
    public GameMapRoot ActiveMapRoot => activeMapRoot;
    public bool IsReadyForMatch => readyForMatch;

    IGameMapCatalog IGameMapSessionService.Catalog => catalog;

    public bool Construct(
        IProjectSceneRegistry projectSceneRegistry,
        NetworkManager runtimeNetworkManager)
    {
        if (projectSceneRegistry == null || runtimeNetworkManager == null)
        {
            Debug.LogError(
                $"{nameof(GameMapService)} requires scene registry and network manager dependencies.",
                this);

            return false;
        }

        sceneRegistry = projectSceneRegistry;
        networkManager = runtimeNetworkManager;
        return true;
    }

    private void Awake()
    {
        ResolveDefaultSelection();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleUnitySceneLoaded;
        SceneManager.sceneUnloaded += HandleUnitySceneUnloaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleUnitySceneLoaded;
        SceneManager.sceneUnloaded -= HandleUnitySceneUnloaded;
        CancelPending(ProjectOperationCancelReason.OwnerDisabled);
    }

    public bool SelectMap(int mapId)
    {
        if (!HasValidCatalog())
            return false;

        if (!catalog.TryGetMap(mapId, out GameMapDefinition map))
        {
            Debug.LogError($"Cannot select unknown game map id {mapId}.", this);
            return false;
        }

        selectedMap = map;
        readyForMatch = false;
        return true;
    }

    public bool CanHandle(ProjectSceneKind sceneKind)
    {
        return sceneKind == ProjectSceneKind.Game;
    }

    public bool Validate(ProjectSceneKind sceneKind, out string error)
    {
        error = string.Empty;

        if (!CanHandle(sceneKind))
            return true;

        if (!HasValidCatalog(out error))
            return false;

        ResolveDefaultSelection();

        if (selectedMap == null)
        {
            error = "No game map is selected.";
            return false;
        }

        return selectedMap.IsConfigured(out error);
    }

    public bool BeginWait(ProjectSceneKind sceneKind, Action<bool> completed)
    {
        if (!CanHandle(sceneKind))
            return false;

        if (completed == null)
        {
            Debug.LogError($"{nameof(GameMapService)} received an empty completion callback.", this);
            return false;
        }

        if (!Validate(sceneKind, out string error))
        {
            Debug.LogError(error, this);
            return false;
        }

        if (pendingCompletion != null)
        {
            Debug.LogError($"{nameof(GameMapService)} is already loading a map.", this);
            return false;
        }

        if (activeMap == selectedMap && activeMapRoot != null && readyForMatch)
        {
            completed(true);
            return true;
        }

        if (networkManager == null || !networkManager.IsListening)
            return BeginLocalLoad(completed);

        if (!networkManager.IsServer)
        {
            Debug.LogError("Only the server can start a network map load.", this);
            return false;
        }

        if (networkManager.SceneManager == null)
        {
            Debug.LogError($"{nameof(NetworkManager)} has no active {nameof(NetworkSceneManager)}.", this);
            return false;
        }

        pendingCompletion = completed;
        readyForMatch = false;
        operationVersion++;
        cancelledMapLoads.Remove(selectedMap.SceneName);
        SubscribeToNetworkLoad();

        SceneEventProgressStatus status = networkManager.SceneManager.LoadScene(
            selectedMap.SceneName,
            LoadSceneMode.Additive);

        if (status == SceneEventProgressStatus.Started)
            return true;

        Debug.LogError(
            $"Failed to start network loading map '{selectedMap.DisplayName}'. Status: {status}.",
            this);

        UnsubscribeFromNetworkLoad();
        CompletePending(false);
        return false;
    }

    public bool TryGetPlayerSpawn(ulong clientId, out Vector3 position, out Quaternion rotation)
    {
        if (activeMapRoot != null &&
            activeMapRoot.TryGetPlayerSpawn(clientId, out position, out rotation))
        {
            return true;
        }

        position = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }

    private bool BeginLocalLoad(Action<bool> completed)
    {
        Scene existingScene = SceneManager.GetSceneByName(selectedMap.SceneName);

        if (existingScene.IsValid() && existingScene.isLoaded)
        {
            CacheLoadedMap(existingScene);
            bool success = activeMapRoot != null;
            readyForMatch = success;
            completed(success);

            if (success)
                MapReady?.Invoke();

            return true;
        }

        pendingCompletion = completed;
        readyForMatch = false;
        localLoadRequested = true;
        int requestedOperationVersion = ++operationVersion;
        GameMapDefinition requestedMap = selectedMap;
        cancelledMapLoads.Remove(requestedMap.SceneName);

        AsyncOperation operation = SceneManager.LoadSceneAsync(
            selectedMap.SceneName,
            LoadSceneMode.Additive);

        if (operation != null)
        {
            operation.completed += _ =>
                HandleLocalLoadOperationCompleted(requestedOperationVersion, requestedMap);

            return true;
        }

        localLoadRequested = false;
        CompletePending(false);
        return false;
    }

    private void HandleUnitySceneLoaded(Scene scene, LoadSceneMode loadMode)
    {
        if (catalog != null &&
            catalog.TryGetMap(scene.name, scene.path, out GameMapDefinition loadedMap))
        {
            if (cancelledMapLoads.Remove(scene.name))
            {
                AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(scene);

                if (unloadOperation == null)
                {
                    Debug.LogError(
                        $"Failed to unload cancelled map scene '{scene.name}'.",
                        this);
                }

                return;
            }

            CacheLoadedMap(scene, loadedMap);
            return;
        }

        if (loadMode == LoadSceneMode.Additive)
            return;

        if (sceneRegistry == null ||
            sceneRegistry.GetSceneKind(scene.name, scene.path) != ProjectSceneKind.Game ||
            (networkManager != null && networkManager.IsListening) ||
            localLoadRequested)
        {
            return;
        }

        ResolveDefaultSelection();

        if (selectedMap == null)
            return;

        BeginLocalLoad(success =>
        {
            if (!success)
                Debug.LogError($"Failed to load local game map '{selectedMap.DisplayName}'.", this);
        });
    }

    private void HandleUnitySceneUnloaded(Scene scene)
    {
        if (activeMap == null || !activeMap.MatchesScene(scene.name, scene.path))
            return;

        activeMap = null;
        activeMapRoot = null;
        readyForMatch = false;
    }

    private void HandleNetworkLoadEventCompleted(
        string sceneName,
        LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted,
        List<ulong> clientsTimedOut)
    {
        if (pendingCompletion == null ||
            selectedMap == null ||
            loadSceneMode != LoadSceneMode.Additive ||
            !string.Equals(sceneName, selectedMap.SceneName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        UnsubscribeFromNetworkLoad();

        Scene loadedScene = SceneManager.GetSceneByName(sceneName);
        CacheLoadedMap(loadedScene);

        bool success = activeMapRoot != null &&
                       (clientsTimedOut == null || clientsTimedOut.Count == 0);

        readyForMatch = success;
        CompletePending(success);

        if (success)
        {
            MapReady?.Invoke();
            return;
        }

        Debug.LogError(
            $"Network map load for '{selectedMap.DisplayName}' did not complete successfully for all clients.",
            this);
    }

    public void CancelPending(ProjectOperationCancelReason reason)
    {
        bool hadPendingOperation = pendingCompletion != null ||
                                   localLoadRequested ||
                                   networkLoadSubscribed;

        if (hadPendingOperation && selectedMap != null)
            cancelledMapLoads.Add(selectedMap.SceneName);

        operationVersion++;
        localLoadRequested = false;
        readyForMatch = false;
        pendingCompletion = null;
        UnsubscribeFromNetworkLoad();

        if (hadPendingOperation && activeMap == selectedMap && !readyForMatch)
        {
            activeMap = null;
            activeMapRoot = null;
        }

        if (hadPendingOperation)
            RuntimeLog.Info($"Cancelled pending game map operation. Reason: {reason}.", this);
    }

    private void HandleLocalLoadOperationCompleted(
        int requestedOperationVersion,
        GameMapDefinition requestedMap)
    {
        if (requestedOperationVersion != operationVersion ||
            !localLoadRequested ||
            pendingCompletion == null ||
            requestedMap == null)
        {
            return;
        }

        localLoadRequested = false;

        Scene loadedScene = SceneManager.GetSceneByName(requestedMap.SceneName);
        CacheLoadedMap(loadedScene, requestedMap);

        bool success = activeMapRoot != null;
        readyForMatch = success;
        CompletePending(success);

        if (success)
            MapReady?.Invoke();
    }

    private void CacheLoadedMap(Scene scene)
    {
        if (catalog == null ||
            !catalog.TryGetMap(scene.name, scene.path, out GameMapDefinition map))
        {
            return;
        }

        CacheLoadedMap(scene, map);
    }

    private void CacheLoadedMap(Scene scene, GameMapDefinition map)
    {
        activeMap = map;
        activeMapRoot = FindMapRoot(scene);

        ComposeMapRuntime(scene);

        if (activeMapRoot == null)
        {
            Debug.LogError(
                $"Map scene '{scene.name}' requires one {nameof(GameMapRoot)}.",
                this);
        }

        // Game remains the active shell scene because project transitions derive
        // their current ProjectSceneKind from Unity's active scene.
    }

    private void ComposeMapRuntime(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        GameObject[] roots = scene.GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            RuntimeNavMeshBuilder[] builders =
                roots[i].GetComponentsInChildren<RuntimeNavMeshBuilder>(true);

            for (int j = 0; j < builders.Length; j++)
                builders[j]?.Construct(this, networkManager);
        }
    }

    private static GameMapRoot FindMapRoot(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return null;

        GameObject[] roots = scene.GetRootGameObjects();
        GameMapRoot foundRoot = null;

        for (int i = 0; i < roots.Length; i++)
        {
            GameMapRoot candidate = roots[i].GetComponentInChildren<GameMapRoot>(true);

            if (candidate == null)
                continue;

            if (foundRoot != null)
            {
                Debug.LogError($"Map scene '{scene.name}' contains more than one {nameof(GameMapRoot)}.");
                return null;
            }

            foundRoot = candidate;
        }

        return foundRoot;
    }

    private void ResolveDefaultSelection()
    {
        if (selectedMap != null || catalog == null)
            return;

        catalog.TryGetMap(catalog.DefaultMapId, out selectedMap);
    }

    private bool HasValidCatalog()
    {
        return HasValidCatalog(out _);
    }

    private bool HasValidCatalog(out string error)
    {
        if (catalog == null)
        {
            error = $"{nameof(GameMapService)} is missing {nameof(GameMapCatalog)}.";
            Debug.LogError(error, this);
            return false;
        }

        if (catalog.IsValid(out error))
            return true;

        Debug.LogError(error, catalog);
        return false;
    }

    private void SubscribeToNetworkLoad()
    {
        if (networkLoadSubscribed)
            return;

        networkManager.SceneManager.OnLoadEventCompleted += HandleNetworkLoadEventCompleted;
        networkLoadSubscribed = true;
    }

    private void UnsubscribeFromNetworkLoad()
    {
        if (!networkLoadSubscribed)
            return;

        if (networkManager != null && networkManager.SceneManager != null)
        {
            networkManager.SceneManager.OnLoadEventCompleted -= HandleNetworkLoadEventCompleted;
        }

        networkLoadSubscribed = false;
    }

    private void CompletePending(bool success)
    {
        Action<bool> completion = pendingCompletion;
        pendingCompletion = null;
        completion?.Invoke(success);
    }
}
