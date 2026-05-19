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

        if (IsNetworkScene(scene.Kind))
            return LoadNetworkScene(scene);

        if (IsNetworkSessionActive())
        {
            Debug.LogError(
                $"Cannot load local scene '{scene.Kind}' while NetworkManager is listening. Shutdown network session first.",
                this);
            return false;
        }

        return localSceneLoader.Load(scene);
    }

    private bool LoadNetworkScene(ProjectSceneDefinition scene)
    {
        if (IsNetworkSessionActive())
            return networkSceneLoader.Load(scene.Kind);

#if UNITY_EDITOR
        if (projectContext.CanStartDirectly(scene.ScenePath))
            return localSceneLoader.Load(scene);
#endif

        Debug.LogError(
            $"Cannot load network scene '{scene.Kind}' without an active network session.",
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

    private static bool IsNetworkScene(ProjectSceneKind sceneKind)
    {
        return sceneKind == ProjectSceneKind.Lobby ||
               sceneKind == ProjectSceneKind.Game;
    }

    private static bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null &&
               NetworkManager.Singleton.IsListening;
    }
}