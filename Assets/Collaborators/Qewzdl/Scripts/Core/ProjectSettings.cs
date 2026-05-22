using UnityEngine;

[CreateAssetMenu(menuName = "Wherever I Am/Project Settings", fileName = "ProjectSettings")]
public sealed class ProjectSettings : ScriptableObject
{
    public const string DefaultAssetPath = "Assets/Collaborators/Qewzdl/Settings/ProjectSettings.asset";

    private static readonly ProjectSceneDefinition[] DefaultScenes =
    {
        new ProjectSceneDefinition(
            ProjectSceneKind.Bootstrap,
            "Bootstrap",
            "Assets/Collaborators/Qewzdl/Scenes/Bootstrap.unity",
            GameState.Bootstrapping),
        new ProjectSceneDefinition(
            ProjectSceneKind.MainMenu,
            "Main Menu",
            "Assets/Collaborators/Qewzdl/Scenes/Main Menu.unity",
            GameState.MainMenu),
        new ProjectSceneDefinition(
            ProjectSceneKind.Lobby,
            "Lobby",
            "Assets/Collaborators/Qewzdl/Scenes/Lobby.unity",
            GameState.Lobby),
        new ProjectSceneDefinition(
            ProjectSceneKind.Game,
            "Game",
            "Assets/Collaborators/Qewzdl/Scenes/Game.unity",
            GameState.InGame),
        new ProjectSceneDefinition(
            ProjectSceneKind.GameplayTest,
            "Test",
            "Assets/Collaborators/6aTowKa/Scenes/Test.unity",
            GameState.InGame)
    };

    [Header("Startup")]
    [SerializeField] private ProjectSceneKind bootstrapScene = ProjectSceneKind.Bootstrap;
    [SerializeField] private ProjectSceneKind defaultStartupScene = ProjectSceneKind.MainMenu;

    [Header("Scenes")]
    [SerializeField] private ProjectSceneDefinition[] scenes =
    {
        new ProjectSceneDefinition(
            ProjectSceneKind.Bootstrap,
            "Bootstrap",
            "Assets/Collaborators/Qewzdl/Scenes/Bootstrap.unity",
            GameState.Bootstrapping),
        new ProjectSceneDefinition(
            ProjectSceneKind.MainMenu,
            "Main Menu",
            "Assets/Collaborators/Qewzdl/Scenes/Main Menu.unity",
            GameState.MainMenu),
        new ProjectSceneDefinition(
            ProjectSceneKind.Lobby,
            "Lobby",
            "Assets/Collaborators/Qewzdl/Scenes/Lobby.unity",
            GameState.Lobby),
        new ProjectSceneDefinition(
            ProjectSceneKind.Game,
            "Game",
            "Assets/Collaborators/Qewzdl/Scenes/Game.unity",
            GameState.InGame),
        new ProjectSceneDefinition(
            ProjectSceneKind.GameplayTest,
            "Test",
            "Assets/Collaborators/6aTowKa/Scenes/Test.unity",
            GameState.InGame)
    };

    public ProjectSceneKind BootstrapScene => bootstrapScene;
    public ProjectSceneKind DefaultStartupScene => defaultStartupScene;

    public string GetSceneName(ProjectSceneKind kind)
    {
        return TryGetScene(kind, out ProjectSceneDefinition scene)
            ? scene.SceneName
            : string.Empty;
    }

    public string GetScenePath(ProjectSceneKind kind)
    {
        return TryGetScene(kind, out ProjectSceneDefinition scene)
            ? scene.ScenePath
            : string.Empty;
    }

    public bool TryGetScene(ProjectSceneKind kind, out ProjectSceneDefinition scene)
    {
        return TryGetScene(kind, scenes, out scene);
    }

    public bool TryGetScene(string sceneName, string scenePath, out ProjectSceneDefinition scene)
    {
        if (scenes != null)
        {
            for (int i = 0; i < scenes.Length; i++)
            {
                ProjectSceneDefinition candidate = scenes[i];

                if (candidate.Matches(sceneName, scenePath))
                {
                    scene = candidate;
                    return true;
                }
            }
        }

        scene = default;
        return false;
    }

    public ProjectSceneKind GetSceneKind(string sceneName, string scenePath)
    {
        return TryGetScene(sceneName, scenePath, out ProjectSceneDefinition scene)
            ? scene.Kind
            : ProjectSceneKind.Unknown;
    }

    public static bool TryGetDefaultScene(ProjectSceneKind kind, out ProjectSceneDefinition scene)
    {
        return TryGetScene(kind, DefaultScenes, out scene);
    }

    public static ProjectSceneKind GetDefaultSceneKind(string sceneName, string scenePath)
    {
        for (int i = 0; i < DefaultScenes.Length; i++)
        {
            ProjectSceneDefinition scene = DefaultScenes[i];

            if (scene.Matches(sceneName, scenePath))
                return scene.Kind;
        }

        return ProjectSceneKind.Unknown;
    }

    private static bool TryGetScene(
        ProjectSceneKind kind,
        ProjectSceneDefinition[] source,
        out ProjectSceneDefinition scene)
    {
        if (source != null)
        {
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i].Kind != kind)
                    continue;

                scene = source[i];
                return true;
            }
        }

        scene = default;
        return false;
    }
}
