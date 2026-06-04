using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class ReleaseBuildDebugComponentStripper : IProcessSceneWithReport
{
    private static readonly HashSet<string> DebugScriptGuids = new()
    {
        "99d6e65315afea44cb27d911e2c130f2",
        "a308a26b06d09864b91e957268b5dbd3",
        "b85ba43aab7d5464081be2f12869b9ac",
        "fde705937b7f62b44a1c9666980924fe",
        "50ae4c129927c3f4eb3d46888c9d49ea",
        "3b9fcfc46d46d4f40a6c02dd2c30cdff",
        "78add1a2accc26045a343cb65202714d"
    };

    public int callbackOrder => 1000;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (!ShouldStrip(report) || !scene.IsValid())
        {
            return;
        }

        int removedCount = StripDebugComponents(scene);

        if (removedCount <= 0)
        {
            return;
        }

        Debug.Log(
            $"{nameof(ReleaseBuildDebugComponentStripper)} stripped {removedCount} " +
            $"debug component(s) from release build scene '{scene.path}'."
        );
    }

    private static bool ShouldStrip(BuildReport report)
    {
        BuildOptions options = report != null
            ? report.summary.options
            : EditorUserBuildSettings.development
                ? BuildOptions.Development
                : BuildOptions.None;

        return (options & BuildOptions.Development) == 0;
    }

    private static int StripDebugComponents(Scene scene)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        int removedCount = 0;

        for (int i = 0; i < roots.Length; i++)
        {
            removedCount += StripDebugComponents(roots[i]);
        }

        return removedCount;
    }

    private static int StripDebugComponents(GameObject root)
    {
        MonoBehaviour[] components = root.GetComponentsInChildren<MonoBehaviour>(true);
        int removedCount = 0;

        for (int i = 0; i < components.Length; i++)
        {
            if (!IsDebugComponent(components[i]))
            {
                continue;
            }

            UnityEngine.Object.DestroyImmediate(components[i]);
            removedCount++;
        }

        return removedCount;
    }

    private static bool IsDebugComponent(MonoBehaviour component)
    {
        if (component == null)
        {
            return false;
        }

        if (component is RuntimeDebugOverlayController || component is RuntimeDebugPanelSource)
        {
            return true;
        }

        MonoScript script = MonoScript.FromMonoBehaviour(component);

        if (script == null)
        {
            return false;
        }

        string scriptPath = AssetDatabase.GetAssetPath(script);

        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            return false;
        }

        return DebugScriptGuids.Contains(AssetDatabase.AssetPathToGUID(scriptPath));
    }
}
