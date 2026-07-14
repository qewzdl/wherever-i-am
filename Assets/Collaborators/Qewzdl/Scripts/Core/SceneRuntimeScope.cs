using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class SceneRuntimeScope : IDisposable
{
    private readonly int sceneHandle;
    private readonly string sceneLabel;
    private readonly SceneRuntimeFeature[] features;
    private int installedFeatureCount;
    private bool disposed;
    private bool ready;

    internal SceneRuntimeScope(
        int handle,
        string label,
        SceneRuntimeFeature[] sceneFeatures)
    {
        sceneHandle = handle;
        sceneLabel = label;
        features = sceneFeatures;
    }

    public int SceneHandle => sceneHandle;
    public bool IsReady => ready && !disposed;

    internal bool Install(ProjectContext context)
    {
        if (IsReady)
            return true;

        if (disposed)
        {
            Debug.LogError($"Scene scope '{sceneLabel}' ({sceneHandle}) is already disposed.");
            return false;
        }

        for (int i = 0; i < features.Length; i++)
        {
            SceneRuntimeFeature feature = features[i];

            if (feature != null && feature.Validate(context))
                continue;

            Debug.LogError(
                $"Scene feature validation failed at index {i} in '{sceneLabel}' ({sceneHandle}).",
                feature);

            return false;
        }

        for (int i = 0; i < features.Length; i++)
        {
            SceneRuntimeFeature feature = features[i];

            if (feature != null && feature.Install(context))
            {
                installedFeatureCount++;
                continue;
            }

            Debug.LogError(
                $"Scene feature install failed: '{GetFeatureName(feature)}' in '{sceneLabel}' ({sceneHandle}).",
                feature);

            RollbackInstalledFeatures();
            return false;
        }

        ready = true;
        return true;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        ready = false;
        RollbackInstalledFeatures();
    }

    private void RollbackInstalledFeatures()
    {
        for (int i = installedFeatureCount - 1; i >= 0; i--)
        {
            SceneRuntimeFeature feature = features[i];

            if (feature != null)
                feature.Uninstall();
        }

        installedFeatureCount = 0;
    }

    private static string GetFeatureName(SceneRuntimeFeature feature)
    {
        return feature != null
            ? feature.GetType().Name
            : "Missing";
    }
}

public sealed class SceneRuntimeScopeRegistry : IDisposable
{
    private readonly Dictionary<int, SceneRuntimeScope> scopes = new();
    private readonly List<int> scopeOrder = new();

    public int Count => scopes.Count;

    public bool Install(Scene scene, ProjectContext context)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return false;

        if (context == null || !context.IsReady)
        {
            Debug.LogError(
                $"Cannot install scene scope '{GetSceneLabel(scene)}' before {nameof(ProjectContext)} is ready.");

            return false;
        }

        if (scopes.TryGetValue(scene.handle, out SceneRuntimeScope existingScope))
            return existingScope.IsReady;

        if (!TryCreateScope(scene, context, out SceneRuntimeScope scope))
            return false;

        scopes.Add(scene.handle, scope);
        scopeOrder.Add(scene.handle);

        if (scope.Install(context))
            return true;

        scopes.Remove(scene.handle);
        scopeOrder.Remove(scene.handle);
        scope.Dispose();
        return false;
    }

    public bool Uninstall(Scene scene)
    {
        return scene.IsValid() && Uninstall(scene.handle);
    }

    public bool Uninstall(int sceneHandle)
    {
        if (!scopes.Remove(sceneHandle, out SceneRuntimeScope scope))
            return false;

        scopeOrder.Remove(sceneHandle);
        scope.Dispose();
        return true;
    }

    public bool TryGetScope(int sceneHandle, out SceneRuntimeScope scope)
    {
        return scopes.TryGetValue(sceneHandle, out scope);
    }

    public void Dispose()
    {
        if (scopes.Count == 0)
            return;

        for (int i = scopeOrder.Count - 1; i >= 0; i--)
        {
            int sceneHandle = scopeOrder[i];

            if (scopes.TryGetValue(sceneHandle, out SceneRuntimeScope scope))
                scope.Dispose();
        }

        scopes.Clear();
        scopeOrder.Clear();
    }

    private static bool TryCreateScope(
        Scene scene,
        ProjectContext context,
        out SceneRuntimeScope scope)
    {
        scope = null;
        List<SceneRuntimeFeature> features = new();
        HashSet<SceneRuntimeFeature> uniqueFeatures = new();
        bool foundRuntime = false;
        bool valid = true;
        GameObject[] roots = scene.GetRootGameObjects();

        for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
        {
            SceneRuntime[] runtimes = roots[rootIndex].GetComponentsInChildren<SceneRuntime>(true);

            for (int runtimeIndex = 0; runtimeIndex < runtimes.Length; runtimeIndex++)
            {
                SceneRuntime runtime = runtimes[runtimeIndex];

                if (runtime == null)
                    continue;

                foundRuntime = true;
                runtime.ValidateSceneKind(context);

                SceneRuntimeFeature[] runtimeFeatures = runtime.Features;

                if (runtimeFeatures == null || runtimeFeatures.Length == 0)
                {
                    Debug.LogError(
                        $"{nameof(SceneRuntime)} on scene '{GetSceneLabel(scene)}' has no feature references.",
                        runtime);

                    valid = false;
                    continue;
                }

                for (int featureIndex = 0; featureIndex < runtimeFeatures.Length; featureIndex++)
                {
                    SceneRuntimeFeature feature = runtimeFeatures[featureIndex];

                    if (feature == null)
                    {
                        Debug.LogError(
                            $"Feature reference is null in {nameof(SceneRuntime)} on scene " +
                            $"'{GetSceneLabel(scene)}' at index {featureIndex}.",
                            runtime);

                        valid = false;
                        continue;
                    }

                    if (!uniqueFeatures.Add(feature))
                    {
                        Debug.LogError(
                            $"Scene feature '{feature.GetType().Name}' is registered more than once " +
                            $"in scene '{GetSceneLabel(scene)}'.",
                            feature);

                        valid = false;
                        continue;
                    }

                    features.Add(feature);
                }
            }
        }

        ProjectSceneKind sceneKind = context.GetSceneKind(scene.name, scene.path);

        if (!foundRuntime)
        {
            if (sceneKind != ProjectSceneKind.Unknown &&
                sceneKind != context.GetBootstrapSceneKind())
            {
                Debug.LogWarning(
                    $"Scene '{scene.name}' has no {nameof(SceneRuntime)}. " +
                    "Scene-level dependencies were not installed.");
            }

            return false;
        }

        if (!valid || features.Count == 0)
        {
            Debug.LogError(
                $"Scene '{scene.name}' has {nameof(SceneRuntime)}, but its scope configuration is invalid.");

            return false;
        }

        scope = new SceneRuntimeScope(
            scene.handle,
            GetSceneLabel(scene),
            features.ToArray());

        return true;
    }

    private static string GetSceneLabel(Scene scene)
    {
        if (!string.IsNullOrWhiteSpace(scene.path))
            return scene.path;

        if (!string.IsNullOrWhiteSpace(scene.name))
            return scene.name;

        return "Unknown";
    }
}
