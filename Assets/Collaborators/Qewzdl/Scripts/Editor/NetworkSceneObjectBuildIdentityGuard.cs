using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Prevents Unity from producing different NGO identifiers for the same in-scene
/// NetworkObject in the Editor and in a player build.
/// </summary>
public sealed class NetworkSceneObjectBuildIdentityGuard : IPreprocessBuildWithReport
{
    private const string NormalizeMenuPath =
        "Tools/Wherever I Am/Networking/Normalize Build-Unstable Scene NetworkObjects";

    private static readonly MethodInfo NetworkObjectOnValidate = typeof(NetworkObject).GetMethod(
        "OnValidate",
        BindingFlags.Instance | BindingFlags.NonPublic
    );

    public int callbackOrder => -900;

    public void OnPreprocessBuild(BuildReport report)
    {
        List<string> unstableObjects = FindUnstableObjectsInBuildScenes();

        if (unstableObjects.Count == 0)
        {
            return;
        }

        StringBuilder message = new(
            "Player build was stopped because in-scene NetworkObject prefab instances " +
            "would receive different GlobalObjectIdHash values in the Editor and the build. " +
            $"Run '{NormalizeMenuPath}' and save the affected scenes."
        );

        for (int i = 0; i < unstableObjects.Count; i++)
        {
            message.Append("\n- ");
            message.Append(unstableObjects[i]);
        }

        throw new BuildFailedException(message.ToString());
    }

    [MenuItem(NormalizeMenuPath)]
    public static void NormalizeBuildUnstableSceneNetworkObjects()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "Scene NetworkObject identities cannot be normalized in Play Mode."
            );
        }

        EnsureLoadedBuildScenesAreSaved();

        Scene originalActiveScene = SceneManager.GetActiveScene();
        int normalizedPrefabRoots = 0;
        int normalizedNetworkObjects = 0;
        int changedScenes = 0;

        try
        {
            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;

            for (int i = 0; i < buildScenes.Length; i++)
            {
                EditorBuildSettingsScene buildScene = buildScenes[i];

                if (!buildScene.enabled || string.IsNullOrWhiteSpace(buildScene.path))
                {
                    continue;
                }

                NormalizeScene(
                    buildScene.path,
                    ref normalizedPrefabRoots,
                    ref normalizedNetworkObjects,
                    ref changedScenes
                );
            }
        }
        finally
        {
            if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(originalActiveScene);
            }
        }

        Debug.Log(
            $"{nameof(NetworkSceneObjectBuildIdentityGuard)} normalized " +
            $"{normalizedNetworkObjects} NetworkObject(s) in {normalizedPrefabRoots} " +
            $"prefab root(s) across {changedScenes} scene(s)."
        );
    }

    private static List<string> FindUnstableObjectsInBuildScenes()
    {
        List<string> unstableObjects = new();
        Scene originalActiveScene = SceneManager.GetActiveScene();

        try
        {
            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;

            for (int i = 0; i < buildScenes.Length; i++)
            {
                EditorBuildSettingsScene buildScene = buildScenes[i];

                if (!buildScene.enabled || string.IsNullOrWhiteSpace(buildScene.path))
                {
                    continue;
                }

                Scene scene = SceneManager.GetSceneByPath(buildScene.path);
                bool openedForValidation = !scene.IsValid() || !scene.isLoaded;

                if (openedForValidation)
                {
                    scene = EditorSceneManager.OpenScene(
                        buildScene.path,
                        OpenSceneMode.Additive
                    );
                }

                try
                {
                    NetworkObject[] networkObjects = GetNetworkObjects(scene);

                    for (int objectIndex = 0;
                         objectIndex < networkObjects.Length;
                         objectIndex++)
                    {
                        NetworkObject networkObject = networkObjects[objectIndex];

                        if (!IsBuildUnstablePrefabInstance(networkObject))
                        {
                            continue;
                        }

                        unstableObjects.Add(
                            $"{scene.path}: {GetHierarchyPath(networkObject.transform)}"
                        );
                    }
                }
                finally
                {
                    if (openedForValidation && scene.IsValid() && scene.isLoaded)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }
        }
        finally
        {
            if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(originalActiveScene);
            }
        }

        return unstableObjects;
    }

    private static void NormalizeScene(
        string scenePath,
        ref int normalizedPrefabRoots,
        ref int normalizedNetworkObjects,
        ref int changedScenes
    )
    {
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool openedForNormalization = !scene.IsValid() || !scene.isLoaded;

        if (openedForNormalization)
        {
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        }

        try
        {
            HashSet<GameObject> prefabRoots = CollectUnstablePrefabRoots(scene);

            if (prefabRoots.Count == 0)
            {
                return;
            }

            foreach (GameObject prefabRoot in prefabRoots)
            {
                PrefabUtility.UnpackPrefabInstance(
                    prefabRoot,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction
                );

                NetworkObject[] unpackedNetworkObjects =
                    prefabRoot.GetComponentsInChildren<NetworkObject>(true);

                for (int i = 0; i < unpackedNetworkObjects.Length; i++)
                {
                    RefreshNetworkObjectIdentity(unpackedNetworkObjects[i]);
                }

                normalizedPrefabRoots++;
                normalizedNetworkObjects += unpackedNetworkObjects.Length;
            }

            EditorSceneManager.MarkSceneDirty(scene);

            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new InvalidOperationException(
                    $"Failed to save normalized scene '{scenePath}'."
                );
            }

            changedScenes++;
        }
        finally
        {
            if (openedForNormalization && scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    private static HashSet<GameObject> CollectUnstablePrefabRoots(Scene scene)
    {
        HashSet<GameObject> prefabRoots = new();
        NetworkObject[] networkObjects = GetNetworkObjects(scene);

        for (int i = 0; i < networkObjects.Length; i++)
        {
            NetworkObject networkObject = networkObjects[i];

            if (!IsBuildUnstablePrefabInstance(networkObject))
            {
                continue;
            }

            GameObject prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(
                networkObject.gameObject
            );

            if (prefabRoot == null)
            {
                throw new InvalidOperationException(
                    $"Could not resolve the prefab root for in-scene NetworkObject " +
                    $"'{GetHierarchyPath(networkObject.transform)}'."
                );
            }

            prefabRoots.Add(prefabRoot);
        }

        return prefabRoots;
    }

    private static bool IsBuildUnstablePrefabInstance(NetworkObject networkObject)
    {
        if (networkObject == null ||
            !networkObject.gameObject.scene.IsValid() ||
            !PrefabUtility.IsPartOfPrefabInstance(networkObject))
        {
            return false;
        }

        GlobalObjectId globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(networkObject);

        // Unity 6 remaps 64-bit prefab instance identifiers while flattening a scene for
        // a Player build. NGO hashes that identifier, so Editor and Player disagree.
        return globalObjectId.targetPrefabId > uint.MaxValue;
    }

    private static NetworkObject[] GetNetworkObjects(Scene scene)
    {
        List<NetworkObject> networkObjects = new();
        GameObject[] roots = scene.GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            networkObjects.AddRange(
                roots[i].GetComponentsInChildren<NetworkObject>(true)
            );
        }

        return networkObjects.ToArray();
    }

    private static void RefreshNetworkObjectIdentity(NetworkObject networkObject)
    {
        if (NetworkObjectOnValidate == null)
        {
            throw new MissingMethodException(
                typeof(NetworkObject).FullName,
                "OnValidate"
            );
        }

        NetworkObjectOnValidate.Invoke(networkObject, null);
        EditorUtility.SetDirty(networkObject);

        if (networkObject.PrefabIdHash == 0)
        {
            throw new InvalidOperationException(
                $"NetworkObject '{GetHierarchyPath(networkObject.transform)}' did not " +
                "receive a valid GlobalObjectIdHash after prefab unpacking."
            );
        }
    }

    private static void EnsureLoadedBuildScenesAreSaved()
    {
        EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;

        for (int i = 0; i < buildScenes.Length; i++)
        {
            if (!buildScenes[i].enabled)
            {
                continue;
            }

            Scene scene = SceneManager.GetSceneByPath(buildScenes[i].path);

            if (scene.IsValid() && scene.isLoaded && scene.isDirty)
            {
                throw new InvalidOperationException(
                    $"Save scene '{scene.path}' before normalizing NetworkObject identities."
                );
            }
        }
    }

    private static string GetHierarchyPath(Transform transform)
    {
        Stack<string> names = new();
        Transform current = transform;

        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names);
    }
}
