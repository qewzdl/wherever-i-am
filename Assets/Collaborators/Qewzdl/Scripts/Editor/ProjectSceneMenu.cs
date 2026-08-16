using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Every scene comes from ProjectSettings rather than a path written out here,
// so what opens is what the runtime believes each scene kind is. Maps are not
// listed: Map Manager already opens those, on their own and together with the
// game shell.
public static class ProjectSceneMenu
{
    // Every item under Tools/Wherever I Am sets its priority, so the order is
    // decided rather than inherited from Unity's default of 1000. Consecutive
    // numbers keep a group together; a gap of 11 or more draws a separator.
    private const int MenuPriority = 140;

    [MenuItem("Tools/Wherever I Am/Scenes/Bootstrap", false, MenuPriority)]
    private static void OpenBootstrapScene()
    {
        ProjectSettings settings = ProjectPlayModeStartup.LoadProjectSettings();

        if (settings == null)
            return;

        // Asking settings which kind bootstraps, rather than naming it, keeps
        // this opening the scene play mode actually boots through.
        Open(settings, settings.BootstrapScene);
    }

    [MenuItem("Tools/Wherever I Am/Scenes/Main Menu", false, MenuPriority + 1)]
    private static void OpenMainMenuScene()
    {
        Open(ProjectSceneKind.MainMenu);
    }

    [MenuItem("Tools/Wherever I Am/Scenes/Lobby", false, MenuPriority + 2)]
    private static void OpenLobbyScene()
    {
        Open(ProjectSceneKind.Lobby);
    }

    [MenuItem("Tools/Wherever I Am/Scenes/Game", false, MenuPriority + 3)]
    private static void OpenGameScene()
    {
        Open(ProjectSceneKind.Game);
    }

    private static void Open(ProjectSceneKind kind)
    {
        ProjectSettings settings = ProjectPlayModeStartup.LoadProjectSettings();

        if (settings == null)
            return;

        Open(settings, kind);
    }

    private static void Open(ProjectSettings settings, ProjectSceneKind kind)
    {
        if (!settings.TryGetScene(kind, out ProjectSceneDefinition scene) ||
            string.IsNullOrWhiteSpace(scene.ScenePath))
        {
            Debug.LogError($"Scene '{kind}' is not configured in {nameof(ProjectSettings)}.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.ScenePath) == null)
        {
            Debug.LogError($"Scene '{kind}' is missing at '{scene.ScenePath}'.");
            return;
        }

        // Asking first, because opening a scene throws away whatever is in the
        // one currently open.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EditorSceneManager.OpenScene(scene.ScenePath, OpenSceneMode.Single);
    }
}
