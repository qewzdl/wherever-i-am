using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ProjectPlayModeStartup
{
    private const string ProjectSettingsAssetPath = "Assets/Collaborators/Qewzdl/Settings/ProjectSettings.asset";
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
        ProjectSettings settings = LoadProjectSettings();

        if (settings == null)
        {
            ClearSession();
            return;
        }

        string bootstrapScenePath = GetBootstrapScenePath(settings);

        if (string.IsNullOrWhiteSpace(bootstrapScenePath))
        {
            ClearSession();
            return;
        }

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
            UnityEngine.Debug.LogError($"Bootstrap scene asset is missing at '{bootstrapScenePath}'.");
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

    private static ProjectSettings LoadProjectSettings()
    {
        ProjectSettings settings = AssetDatabase.LoadAssetAtPath<ProjectSettings>(ProjectSettingsAssetPath);

        if (settings != null)
            return settings;

        UnityEngine.Debug.LogError($"{nameof(ProjectPlayModeStartup)} cannot load {nameof(ProjectSettings)} at '{ProjectSettingsAssetPath}'.");
        return null;
    }

    private static string GetBootstrapScenePath(ProjectSettings settings)
    {
        if (!settings.TryGetScene(settings.BootstrapScene, out ProjectSceneDefinition bootstrapScene))
        {
            UnityEngine.Debug.LogError($"Bootstrap scene is not configured in {nameof(ProjectSettings)}.");
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(bootstrapScene.ScenePath))
            return bootstrapScene.ScenePath;

        UnityEngine.Debug.LogError($"Bootstrap scene path is empty in {nameof(ProjectSettings)}.");
        return string.Empty;
    }

    private static bool CanStartDirectly(string scenePath, ProjectSettings settings)
    {
        if (!settings.TryGetScene(string.Empty, scenePath, out ProjectSceneDefinition scene))
            return false;

        if (scene.Kind == settings.BootstrapScene)
            return false;

        return AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null;
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