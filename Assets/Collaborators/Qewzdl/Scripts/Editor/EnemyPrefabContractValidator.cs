using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public class EnemyPrefabContractValidator : AssetPostprocessor
{
    private const string EnemyPrefabSearchRoot = "Assets/Collaborators/Qewzdl";
    private const string MenuPath = "Tools/Wherever I Am/Enemies/Validate Enemy Prefab Contracts";

    [MenuItem(MenuPath)]
    private static void ValidateAllEnemyPrefabs()
    {
        string[] prefabGuids = AssetDatabase.FindAssets(
            "t:Prefab",
            new[] { EnemyPrefabSearchRoot }
        );

        int checkedEnemyPrefabs = 0;
        int invalidEnemyPrefabs = 0;

        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefabRoot == null)
            {
                continue;
            }

            if (prefabRoot.GetComponent<NetworkEnemyController>() == null)
            {
                continue;
            }

            checkedEnemyPrefabs++;

            if (!ValidateEnemyPrefab(prefabRoot, prefabPath, false))
            {
                invalidEnemyPrefabs++;
            }
        }

        if (invalidEnemyPrefabs > 0)
        {
            Debug.LogError(
                $"Enemy prefab contract validation failed. Invalid prefabs: " +
                $"{invalidEnemyPrefabs}/{checkedEnemyPrefabs}."
            );

            return;
        }

        Debug.Log(
            $"Enemy prefab contract validation passed. Valid enemy prefabs: {checkedEnemyPrefabs}."
        );
    }

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths
    )
    {
        ValidateChangedPrefabs(importedAssets);
        ValidateChangedPrefabs(movedAssets);
    }

    private static void ValidateChangedPrefabs(string[] assetPaths)
    {
        for (int i = 0; i < assetPaths.Length; i++)
        {
            string assetPath = assetPaths[i];

            if (!assetPath.EndsWith(".prefab"))
            {
                continue;
            }

            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

            if (prefabRoot == null)
            {
                continue;
            }

            if (prefabRoot.GetComponent<NetworkEnemyController>() == null)
            {
                continue;
            }

            ValidateEnemyPrefab(prefabRoot, assetPath, true);
        }
    }

    private static bool ValidateEnemyPrefab(
        GameObject prefabRoot,
        string prefabPath,
        bool logValidPrefab
    )
    {
        List<string> errors = new();

        ValidateRequiredRootComponent<NetworkObject>(prefabRoot, errors);
        ValidateRequiredRootComponent<NetworkTransform>(prefabRoot, errors);
        ValidateRequiredRootComponent<NetworkEnemyController>(prefabRoot, errors);
        ValidateRequiredRootComponent<EnemyNetworkState>(prefabRoot, errors);
        ValidateRequiredRootComponent<EnemyServerRuntime>(prefabRoot, errors);
        ValidateRequiredRootComponent<EnemyNavigator>(prefabRoot, errors);
        ValidateRequiredRootComponent<EnemyPhysicsMotor>(prefabRoot, errors);
        ValidateRequiredRootComponent<EnemyItemPusher>(prefabRoot, errors);
        ValidateRequiredRootComponent<Rigidbody>(prefabRoot, errors);
        ValidateRequiredRootComponent<CapsuleCollider>(prefabRoot, errors);
        ValidateRequiredRootComponent<NavMeshAgent>(prefabRoot, errors);
        ValidateNavMeshAgentStartsDisabled(prefabRoot, errors);

        int missingScriptCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
            prefabRoot
        );

        if (missingScriptCount > 0)
        {
            errors.Add($"Prefab has missing MonoBehaviour scripts: {missingScriptCount}.");
        }

        if (errors.Count > 0)
        {
            Debug.LogError(
                BuildErrorMessage(prefabPath, errors),
                prefabRoot
            );

            return false;
        }

        if (logValidPrefab)
        {
            Debug.Log(
                $"Enemy prefab contract is valid: {prefabPath}",
                prefabRoot
            );
        }

        return true;
    }

    private static void ValidateRequiredRootComponent<TComponent>(
        GameObject prefabRoot,
        List<string> errors
    )
        where TComponent : Component
    {
        if (prefabRoot.GetComponent<TComponent>() != null)
        {
            return;
        }

        errors.Add($"{typeof(TComponent).Name} must be attached to the enemy prefab root.");
    }

    private static void ValidateNavMeshAgentStartsDisabled(
        GameObject prefabRoot,
        List<string> errors)
    {
        NavMeshAgent agent = prefabRoot.GetComponent<NavMeshAgent>();

        if (agent != null && agent.enabled)
        {
            errors.Add(
                $"{nameof(NavMeshAgent)} must start disabled and be enabled by " +
                $"{nameof(EnemyNavMeshStartupGate)} after runtime NavMesh build.");
        }
    }

    private static string BuildErrorMessage(string prefabPath, List<string> errors)
    {
        string message = $"Enemy prefab contract is broken: {prefabPath}";

        for (int i = 0; i < errors.Count; i++)
        {
            message += $"\n- {errors[i]}";
        }

        return message;
    }
}
