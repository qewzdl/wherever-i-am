using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Every scene comes from ProjectSettings rather than a path written out here,
// so what opens is what the runtime believes each scene kind is. Maps are not
// listed: Map Manager already opens those, on their own and together with the
// game shell.
public static class ProjectSceneMenu
{
    // Sits below everything else, one section of its own. Consecutive numbers
    // keep the four together; Unity only draws a separator where neighbouring
    // priorities differ by 11 or more.
    private const int MenuPriority = 2000;

    [MenuItem("Tools/Wherever I Am/Open Bootstrap Scene", false, MenuPriority)]
    private static void OpenBootstrapScene()
    {
        ProjectSettings settings = ProjectPlayModeStartup.LoadProjectSettings();

        if (settings == null)
            return;

        // Asking settings which kind bootstraps, rather than naming it, keeps
        // this opening the scene play mode actually boots through.
        Open(settings, settings.BootstrapScene);
    }

    [MenuItem("Tools/Wherever I Am/Open Main Menu Scene", false, MenuPriority + 1)]
    private static void OpenMainMenuScene()
    {
        Open(ProjectSceneKind.MainMenu);
    }

    [MenuItem("Tools/Wherever I Am/Open Lobby Scene", false, MenuPriority + 2)]
    private static void OpenLobbyScene()
    {
        Open(ProjectSceneKind.Lobby);
    }

    [MenuItem("Tools/Wherever I Am/Open Game Scene", false, MenuPriority + 3)]
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
