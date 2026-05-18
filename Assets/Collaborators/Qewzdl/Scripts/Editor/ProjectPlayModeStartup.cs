using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ProjectPlayModeStartup
{
    private const string PreviousPlayModeStartScenePathKey = "WhereverIAm.Editor.PreviousPlayModeStartScenePath";

    static ProjectPlayModeStartup()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        switch (state)
        {
            case PlayModeStateChange.ExitingEditMode:
                PreparePlayMode();
                break;

            case PlayModeStateChange.EnteredEditMode:
                RestorePlayModeStartScene();
                break;
        }
    }

    private static void PreparePlayMode()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        string activeScenePath = activeScene.path;
        ProjectSettings settings = AssetDatabase.LoadAssetAtPath<ProjectSettings>(ProjectSettings.DefaultAssetPath);
        string bootstrapScenePath = GetBootstrapScenePath(settings);

        if (string.IsNullOrWhiteSpace(activeScenePath))
        {
            ClearSession();
            return;
        }

        if (PathsEqual(activeScenePath, bootstrapScenePath))
        {
            ClearSession();
            return;
        }

        if (!CanStartDirectly(activeScenePath, settings))
        {
            ClearSession();
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            ClearSession();
            EditorApplication.isPlaying = false;
            return;
        }

        string previousStartScenePath = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene);
        SceneAsset bootstrapScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(bootstrapScenePath);

        if (bootstrapScene == null)
        {
            ClearSession();
            return;
        }

        SessionState.SetString(AppRuntime.EditorStartupScenePathKey, activeScenePath);
        SessionState.SetString(PreviousPlayModeStartScenePathKey, previousStartScenePath);

        EditorSceneManager.playModeStartScene = bootstrapScene;
    }

    private static void RestorePlayModeStartScene()
    {
        string previousStartScenePath = SessionState.GetString(PreviousPlayModeStartScenePathKey, string.Empty);
        ClearSession();

        if (string.IsNullOrWhiteSpace(previousStartScenePath))
        {
            EditorSceneManager.playModeStartScene = null;
            return;
        }

        if (!File.Exists(previousStartScenePath))
        {
            EditorSceneManager.playModeStartScene = null;
            return;
        }

        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(previousStartScenePath);
    }

    private static void ClearSession()
    {
        SessionState.EraseString(AppRuntime.EditorStartupScenePathKey);
        SessionState.EraseString(PreviousPlayModeStartScenePathKey);
    }

    private static string GetBootstrapScenePath(ProjectSettings settings)
    {
        if (settings != null)
        {
            string path = settings.GetScenePath(settings.BootstrapScene);

            if (!string.IsNullOrWhiteSpace(path))
                return path;
        }

        return "Assets/Collaborators/Qewzdl/Scenes/Bootstrap.unity";
    }

    private static bool CanStartDirectly(string scenePath, ProjectSettings settings)
    {
        if (settings != null)
            return settings.CanStartDirectly(scenePath) &&
                   AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null;

        if (!ProjectSettings.CanDefaultSceneStartDirectly(scenePath))
            return false;

        return IsEnabledBuildScene(scenePath);
    }

    private static bool IsEnabledBuildScene(string scenePath)
    {
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (!scene.enabled)
                continue;

            if (PathsEqual(scene.path, scenePath))
                return true;
        }

        return false;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/');
    }
}
