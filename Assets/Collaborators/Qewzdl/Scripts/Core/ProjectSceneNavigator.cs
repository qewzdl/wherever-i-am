using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProjectSceneNavigator : MonoBehaviour
{
    [SerializeField] private ProjectContext projectContext;
    [SerializeField] private LocalSceneLoader localSceneLoader;
    [SerializeField] private NetworkSceneLoader networkSceneLoader;

    public string MainMenuSceneName => GetSceneName(ProjectSceneKind.MainMenu);
    public string LobbySceneName => GetSceneName(ProjectSceneKind.Lobby);
    public string GameSceneName => GetSceneName(ProjectSceneKind.Game);

    public void Construct(
        ProjectContext context,
        LocalSceneLoader localLoader,
        NetworkSceneLoader networkLoader)
    {
        projectContext = context;
        localSceneLoader = localLoader;
        networkSceneLoader = networkLoader;

        if (localSceneLoader != null)
            localSceneLoader.Construct(projectContext);

        if (networkSceneLoader != null)
            networkSceneLoader.Construct(projectContext);
    }

    public bool LoadMainMenu()
    {
        return LoadScene(ProjectSceneKind.MainMenu);
    }

    public bool LoadLobby()
    {
        return LoadScene(ProjectSceneKind.Lobby);
    }

    public bool LoadGame()
    {
        return LoadScene(ProjectSceneKind.Game);
    }

    public bool LoadScene(ProjectSceneKind sceneKind)
    {
        if (!HasRequiredReferences())
            return false;

        if (!projectContext.TryGetScene(sceneKind, out ProjectSceneDefinition scene))
        {
            Debug.LogError($"Scene is not configured for {sceneKind}.", this);
            return false;
        }

        ProjectSceneKind currentScene = projectContext.GetActiveSceneKind();

        if (!projectContext.SceneFlow.TryGetTransition(
                currentScene,
                scene.Kind,
                out ProjectSceneTransitionDefinition transition))
        {
            Debug.LogError($"Scene transition is not configured: {currentScene} -> {scene.Kind}.", this);
            return false;
        }

        if (!CanUseTransition(transition))
            return false;

        switch (transition.LoadMode)
        {
            case ProjectSceneLoadMode.Local:
                return LoadLocalScene(scene);

            case ProjectSceneLoadMode.Network:
                return LoadNetworkScene(scene, transition);
        }

        Debug.LogError($"Unsupported scene load mode '{transition.LoadMode}' for transition {currentScene} -> {scene.Kind}.", this);
        return false;
    }

    private bool LoadLocalScene(ProjectSceneDefinition scene)
    {
        if (IsNetworkSessionActive())
        {
            Debug.LogError(
                $"Cannot load local scene '{scene.Kind}' while NetworkManager is listening. Shutdown network session first.",
                this);
            return false;
        }

        return localSceneLoader.Load(scene);
    }

    private bool LoadNetworkScene(
        ProjectSceneDefinition scene,
        ProjectSceneTransitionDefinition transition)
    {
        if (IsNetworkSessionActive())
            return networkSceneLoader.Load(scene.Kind);

#if UNITY_EDITOR
        if (transition.AllowEditorDirectLoad && projectContext.CanStartDirectly(scene.ScenePath))
            return localSceneLoader.Load(scene);
#endif

        Debug.LogError(
            $"Cannot load network scene '{scene.Kind}' without an active network session.",
            this);
        return false;
    }

    private bool CanUseTransition(ProjectSceneTransitionDefinition transition)
    {
        if (transition.Authority == ProjectSceneTransitionAuthority.ServerOnly)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                Debug.LogWarning($"Only server can execute scene transition {transition.FromScene} -> {transition.ToScene}.", this);
                return false;
            }
        }

        if (!transition.RequiresActiveNetworkSession)
            return true;

        if (IsNetworkSessionActive())
            return true;

#if UNITY_EDITOR
        if (transition.AllowEditorDirectLoad)
            return true;
#endif

        Debug.LogError(
            $"Scene transition {transition.FromScene} -> {transition.ToScene} requires an active network session.",
            this);
        return false;
    }

    private string GetSceneName(ProjectSceneKind sceneKind)
    {
        if (projectContext == null)
            return string.Empty;

        return projectContext.GetSceneName(sceneKind);
    }

    private bool HasRequiredReferences()
    {
        if (projectContext == null)
        {
            Debug.LogError($"{nameof(ProjectSceneNavigator)} is missing {nameof(ProjectContext)}.", this);
            return false;
        }

        if (projectContext.SceneFlow == null)
        {
            Debug.LogError($"{nameof(ProjectSceneNavigator)} is missing {nameof(ProjectSceneFlow)}.", this);
            return false;
        }

        if (localSceneLoader == null)
        {
            Debug.LogError($"{nameof(ProjectSceneNavigator)} is missing {nameof(LocalSceneLoader)}.", this);
            return false;
        }

        if (networkSceneLoader == null)
        {
            Debug.LogError($"{nameof(ProjectSceneNavigator)} is missing {nameof(NetworkSceneLoader)}.", this);
            return false;
        }

        return true;
    }

    private static bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null &&
               NetworkManager.Singleton.IsListening;
    }
}