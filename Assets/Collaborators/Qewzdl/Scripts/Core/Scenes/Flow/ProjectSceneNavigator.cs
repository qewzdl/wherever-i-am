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
    }

    public void DisposeComposition()
    {
        projectContext = null;
        localSceneLoader = null;
        networkSceneLoader = null;
    }

    public bool Load(ProjectSceneKind sceneKind, ProjectSceneLoadMode loadMode)
    {
        if (!TryGetScene(sceneKind, out ProjectSceneDefinition scene))
            return false;

        switch (loadMode)
        {
            case ProjectSceneLoadMode.Local:
                return localSceneLoader.Load(scene);

            case ProjectSceneLoadMode.Network:
                return networkSceneLoader.Load(scene.Kind);
        }

        Debug.LogError($"Unsupported scene load mode '{loadMode}' for scene '{sceneKind}'.", this);
        return false;
    }

    public bool LoadLocal(ProjectSceneKind sceneKind)
    {
        if (!TryGetScene(sceneKind, out ProjectSceneDefinition scene))
            return false;

        return localSceneLoader.Load(scene);
    }

    public bool LoadNetwork(ProjectSceneKind sceneKind)
    {
        if (!TryGetScene(sceneKind, out ProjectSceneDefinition scene))
            return false;

        return networkSceneLoader.Load(scene.Kind);
    }

    private bool TryGetScene(ProjectSceneKind sceneKind, out ProjectSceneDefinition scene)
    {
        scene = default;

        if (!HasRequiredReferences())
            return false;

        if (projectContext.TryGetScene(sceneKind, out scene))
            return true;

        Debug.LogError($"Scene is not configured for {sceneKind}.", this);
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
}
