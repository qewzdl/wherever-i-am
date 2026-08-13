using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BootstrapSceneMenu
{
    private const string MenuPath = "Tools/Wherever I Am/Open Bootstrap Scene";

    // Enemies, Maps and Networking sit on the default 1000 and Tests on 1500.
    // Anything at least 11 above the item before it gets a separator drawn, so
    // this both lands last and stands apart from them.
    private const int MenuPriority = 2000;

    [MenuItem(MenuPath, false, MenuPriority)]
    private static void OpenBootstrapScene()
    {
        ProjectSettings settings = ProjectPlayModeStartup.LoadProjectSettings();

        if (settings == null)
            return;

        string bootstrapScenePath = ProjectPlayModeStartup.GetBootstrapScenePath(settings);

        if (string.IsNullOrWhiteSpace(bootstrapScenePath))
            return;

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(bootstrapScenePath) == null)
        {
            Debug.LogError($"Bootstrap scene asset is missing at '{bootstrapScenePath}'.");
            return;
        }

        // Asking first, because opening a scene throws away whatever is in the
        // one currently open.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EditorSceneManager.OpenScene(bootstrapScenePath, OpenSceneMode.Single);
    }
}
