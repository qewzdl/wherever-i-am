using System;
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
            GameState.Bootstrapping,
            false),
        new ProjectSceneDefinition(
            ProjectSceneKind.MainMenu,
            "Main Menu",
            "Assets/Collaborators/Qewzdl/Scenes/Main Menu.unity",
            GameState.MainMenu,
            true),
        new ProjectSceneDefinition(
            ProjectSceneKind.Lobby,
            "Lobby",
            "Assets/Collaborators/Qewzdl/Scenes/Lobby.unity",
            GameState.Lobby,
            true),
        new ProjectSceneDefinition(
            ProjectSceneKind.Game,
            "Game",
            "Assets/Collaborators/Qewzdl/Scenes/Game.unity",
            GameState.InGame,
            true),
        new ProjectSceneDefinition(
            ProjectSceneKind.GameplayTest,
            "Test",
            "Assets/Collaborators/6aTowKa/Scenes/Test.unity",
            GameState.InGame,
            true)
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
            GameState.Bootstrapping,
            false),
        new ProjectSceneDefinition(
            ProjectSceneKind.MainMenu,
            "Main Menu",
            "Assets/Collaborators/Qewzdl/Scenes/Main Menu.unity",
            GameState.MainMenu,
            true),
        new ProjectSceneDefinition(
            ProjectSceneKind.Lobby,
            "Lobby",
            "Assets/Collaborators/Qewzdl/Scenes/Lobby.unity",
            GameState.Lobby,
            true),
        new ProjectSceneDefinition(
            ProjectSceneKind.Game,
            "Game",
            "Assets/Collaborators/Qewzdl/Scenes/Game.unity",
            GameState.InGame,
            true),
        new ProjectSceneDefinition(
            ProjectSceneKind.GameplayTest,
            "Test",
            "Assets/Collaborators/6aTowKa/Scenes/Test.unity",
            GameState.InGame,
            true)
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

    public bool CanStartDirectly(string scenePath)
    {
        if (TryGetScene(string.Empty, scenePath, out ProjectSceneDefinition scene))
            return scene.CanStartDirectly;

        return false;
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

    public static bool CanDefaultSceneStartDirectly(string scenePath)
    {
        for (int i = 0; i < DefaultScenes.Length; i++)
        {
            ProjectSceneDefinition scene = DefaultScenes[i];

            if (scene.Matches(string.Empty, scenePath))
                return scene.CanStartDirectly;
        }

        return false;
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

[Serializable]
public struct ProjectSceneDefinition
{
    [SerializeField] private ProjectSceneKind kind;
    [SerializeField] private string sceneName;
    [SerializeField] private string scenePath;
    [SerializeField] private GameState state;
    [SerializeField] private bool canStartDirectly;

    public ProjectSceneDefinition(
        ProjectSceneKind kind,
        string sceneName,
        string scenePath,
        GameState state,
        bool canStartDirectly)
    {
        this.kind = kind;
        this.sceneName = sceneName;
        this.scenePath = scenePath;
        this.state = state;
        this.canStartDirectly = canStartDirectly;
    }

    public ProjectSceneKind Kind => kind;
    public string SceneName => sceneName;
    public string ScenePath => scenePath;
    public GameState State => state;
    public bool CanStartDirectly => canStartDirectly;

    public bool Matches(string candidateName, string candidatePath)
    {
        return SceneNameEquals(candidateName, sceneName) ||
               ScenePathEquals(candidatePath, scenePath);
    }

    private static bool SceneNameEquals(string left, string right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ScenePathEquals(string left, string right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/');
    }
}
