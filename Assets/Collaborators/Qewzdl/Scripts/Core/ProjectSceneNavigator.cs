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

    public bool Load(ProjectSceneKind sceneKind, ProjectSceneLoadMode loadMode)
    {
        switch (loadMode)
        {
            case ProjectSceneLoadMode.Local:
                return LoadLocal(sceneKind);

            case ProjectSceneLoadMode.Network:
                return LoadNetwork(sceneKind);
        }

        Debug.LogError($"Unsupported scene load mode '{loadMode}' for scene '{sceneKind}'.", this);
        return false;
    }

    public bool LoadLocal(ProjectSceneKind sceneKind)
    {
        if (!HasRequiredReferences())
            return false;

        if (!projectContext.TryGetScene(sceneKind, out ProjectSceneDefinition scene))
        {
            Debug.LogError($"Scene is not configured for {sceneKind}.", this);
            return false;
        }

        return localSceneLoader.Load(scene);
    }

    public bool LoadNetwork(ProjectSceneKind sceneKind)
    {
        if (!HasRequiredReferences())
            return false;

        if (!projectContext.TryGetScene(sceneKind, out ProjectSceneDefinition scene))
        {
            Debug.LogError($"Scene is not configured for {sceneKind}.", this);
            return false;
        }

        return networkSceneLoader.Load(scene.Kind);
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