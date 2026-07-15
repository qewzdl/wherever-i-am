using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

internal enum SceneServiceScopeParent
{
    Global = 0,
    Session = 1
}

public sealed class SceneRuntimeScope : IDisposable
{
    private readonly int sceneHandle;
    private readonly Scene scene;
    private readonly string sceneLabel;
    private readonly SceneServiceScopeParent serviceScopeParent;
    private readonly ServiceScope serviceScope;
    private readonly SceneServiceRegistrar serviceRegistrar;
    private readonly SceneFeatureContext featureContext;
    private readonly SceneRuntimeFeature[] features;
    private AudioServiceComposition audioComposition;
    private int installedFeatureCount;
    private bool disposed;
    private bool ready;

    internal SceneRuntimeScope(
        Scene runtimeScene,
        string label,
        ProjectSceneKind sceneKind,
        SceneServiceScopeParent parent,
        ServiceScope services,
        SceneRuntimeFeature[] sceneFeatures)
        : this(
            runtimeScene,
            runtimeScene.handle,
            label,
            sceneKind,
            parent,
            services,
            sceneFeatures)
    {
    }

    internal SceneRuntimeScope(
        int handle,
        string label,
        ProjectSceneKind sceneKind,
        SceneServiceScopeParent parent,
        ServiceScope services,
        SceneRuntimeFeature[] sceneFeatures)
        : this(
            default,
            handle,
            label,
            sceneKind,
            parent,
            services,
            sceneFeatures)
    {
    }

    private SceneRuntimeScope(
        Scene runtimeScene,
        int handle,
        string label,
        ProjectSceneKind sceneKind,
        SceneServiceScopeParent parent,
        ServiceScope services,
        SceneRuntimeFeature[] sceneFeatures)
    {
        scene = runtimeScene;
        sceneHandle = handle;
        sceneLabel = label;
        serviceScopeParent = parent;
        serviceScope = services ?? throw new ArgumentNullException(nameof(services));
        serviceRegistrar = new SceneServiceRegistrar(serviceScope);
        featureContext = new SceneFeatureContext(
            sceneHandle,
            sceneKind,
            serviceScope,
            serviceRegistrar);
        features = sceneFeatures ?? throw new ArgumentNullException(nameof(sceneFeatures));
    }

    public int SceneHandle => sceneHandle;
    public bool IsReady => ready && !disposed && !serviceScope.IsDisposed;
    public IServiceResolver Services => IsReady
        ? serviceScope
        : null;
    internal SceneServiceScopeParent ServiceScopeParent => serviceScopeParent;

    internal bool Install()
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

            if (feature != null && feature.Validate(featureContext))
                continue;

            Debug.LogError(
                $"Scene feature validation failed at index {i} in '{sceneLabel}' ({sceneHandle}).",
                feature);

            return FailInstall(null);
        }

        ServiceRegistrationTransaction registrationTransaction = null;

        try
        {
            registrationTransaction = serviceScope.BeginRegistrationTransaction();
            serviceRegistrar.BeginRegistration();

            if (scene.IsValid() && scene.isLoaded)
            {
                IAudioService audioService = serviceScope.Resolve<IAudioService>();

                if (!AudioServiceComposition.TryCompose(
                        scene,
                        audioService,
                        out audioComposition))
                {
                    throw new InvalidOperationException(
                        $"Failed to compose audio consumers in scene '{sceneLabel}'.");
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"Failed to begin scene service registration for '{sceneLabel}' ({sceneHandle}).");
            Debug.LogException(exception);
            return FailInstall(registrationTransaction);
        }

        for (int i = 0; i < features.Length; i++)
        {
            SceneRuntimeFeature feature = features[i];

            if (feature != null && feature.InstallValidated(featureContext))
            {
                installedFeatureCount++;
                continue;
            }

            Debug.LogError(
                $"Scene feature install failed: '{GetFeatureName(feature)}' in '{sceneLabel}' ({sceneHandle}).",
                feature);

            return FailInstall(registrationTransaction);
        }

        try
        {
            registrationTransaction.Commit();
            serviceRegistrar.CloseRegistration();
            ready = true;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"Failed to commit scene service registration for '{sceneLabel}' ({sceneHandle}).");
            Debug.LogException(exception);
            return FailInstall(registrationTransaction);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        serviceRegistrar.CloseRegistration();
        disposed = true;
        ready = false;
        RollbackInstalledFeatures();
        DisposeAudioComposition();
        DisposeServiceScope();
    }

    private bool FailInstall(ServiceRegistrationTransaction registrationTransaction)
    {
        serviceRegistrar.CloseRegistration();
        disposed = true;
        ready = false;
        RollbackInstalledFeatures();
        DisposeAudioComposition();
        RollbackRegistrations(registrationTransaction);
        DisposeServiceScope();
        return false;
    }

    private static void RollbackRegistrations(
        ServiceRegistrationTransaction registrationTransaction)
    {
        if (registrationTransaction == null || registrationTransaction.IsCompleted)
            return;

        try
        {
            registrationTransaction.Rollback();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private void DisposeServiceScope()
    {
        try
        {
            serviceScope.Dispose();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private void DisposeAudioComposition()
    {
        AudioServiceComposition composition = audioComposition;
        audioComposition = null;
        composition?.Dispose();
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

        if (scope.Install())
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

    internal int UninstallSessionScopes()
    {
        int uninstallCount = 0;

        for (int i = scopeOrder.Count - 1; i >= 0; i--)
        {
            int sceneHandle = scopeOrder[i];

            if (!scopes.TryGetValue(sceneHandle, out SceneRuntimeScope scope) ||
                scope.ServiceScopeParent != SceneServiceScopeParent.Session)
            {
                continue;
            }

            scopes.Remove(sceneHandle);
            scopeOrder.RemoveAt(i);
            scope.Dispose();
            uninstallCount++;
        }

        return uninstallCount;
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
        bool isMapScene = context.IsGameMapScene(scene);

        if (!foundRuntime)
        {
            bool hasSceneScopePolicy = sceneKind == ProjectSceneKind.MainMenu ||
                                       sceneKind == ProjectSceneKind.Lobby ||
                                       sceneKind == ProjectSceneKind.Game ||
                                       isMapScene;

            if (!hasSceneScopePolicy)
                return false;

            if (!isMapScene)
            {
                Debug.LogWarning(
                    $"Scene '{scene.name}' has no {nameof(SceneRuntime)}. " +
                    "An empty scene service scope will be installed.");
            }
        }

        if (!valid)
        {
            Debug.LogError(
                $"Scene '{scene.name}' has {nameof(SceneRuntime)}, but its scope configuration is invalid.");

            return false;
        }

        if (!context.TryCreateSceneServiceScope(
                scene,
                sceneKind,
                out ServiceScope sceneServiceScope,
                out SceneServiceScopeParent parent))
        {
            return false;
        }

        try
        {
            scope = new SceneRuntimeScope(
                scene,
                GetSceneLabel(scene),
                sceneKind,
                parent,
                sceneServiceScope,
                features.ToArray());

            return true;
        }
        catch (Exception exception)
        {
            try
            {
                sceneServiceScope.Dispose();
            }
            catch (Exception cleanupException)
            {
                Debug.LogException(cleanupException);
            }

            Debug.LogError(
                $"Failed to compose scene runtime scope for '{GetSceneLabel(scene)}'.");
            Debug.LogException(exception);
            return false;
        }
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
