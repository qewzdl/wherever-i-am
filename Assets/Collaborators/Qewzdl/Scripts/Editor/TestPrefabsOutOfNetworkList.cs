using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

// Keeps prefabs that belong to the tests out of the network prefab list the
// game ships with.
//
// Netcode adds every prefab with a NetworkObject it imports to that list, and
// it has no way to leave one out. The list is referenced by the Bootstrap
// scene, so whatever is in it goes into the build - the tests' own enemy, its
// configs and profiles along with it. Taken back out once every import that
// touches a test folder is over, and again before every build. The tests that
// spawn one register it with their own NetworkManager, so they never needed it
// in the list.
public sealed class TestPrefabsOutOfNetworkList : AssetPostprocessor, IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    // A build imports nothing, so Netcode has no chance to put one back
    // between this and the build reading the list.
    public void OnPreprocessBuild(BuildReport report)
    {
        RemoveFromProjectList();
    }

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (!importedAssets.Concat(movedAssets).Any(IsTestPath))
        {
            return;
        }

        // Netcode adds its entry after this runs, whatever order is asked
        // for, so the list is cleaned once the import is over.
        EditorApplication.delayCall += RemoveFromProjectList;
    }

    public static bool IsTestPath(string path)
    {
        return path.Contains("/Tests/");
    }

    private static void RemoveFromProjectList()
    {
        RemoveTestPrefabs(AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(EnemyFactory.NetworkPrefabsPath));
    }

    // Returns how many entries it took out.
    public static int RemoveTestPrefabs(NetworkPrefabsList networkPrefabs)
    {
        if (networkPrefabs == null)
        {
            return 0;
        }

        List<NetworkPrefab> tests = networkPrefabs.PrefabList
            .Where(entry => entry?.Prefab != null && IsTestPath(AssetDatabase.GetAssetPath(entry.Prefab)))
            .ToList();

        foreach (NetworkPrefab entry in tests)
        {
            networkPrefabs.Remove(entry);
        }

        if (tests.Count > 0 && EditorUtility.IsPersistent(networkPrefabs))
        {
            EditorUtility.SetDirty(networkPrefabs);
            AssetDatabase.SaveAssetIfDirty(networkPrefabs);
        }

        return tests.Count;
    }
}
