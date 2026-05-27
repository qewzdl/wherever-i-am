using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-900)]
[DisallowMultipleComponent]
public sealed class SceneRuntime : MonoBehaviour
{
    [SerializeField] private ProjectSceneKind sceneKind = ProjectSceneKind.Unknown;
    [SerializeField] private bool installOnAwake = true;
    [SerializeField] private SceneRuntimeFeature[] features;

    private bool installAttempted;
    private bool installed;

    public ProjectSceneKind SceneKind => sceneKind;

    private void Awake()
    {
        if (installOnAwake)
            Install(ProjectContext.Instance);
    }

    public bool Install(ProjectContext context)
    {
        if (installed)
            return true;

        if (context == null)
        {
            Debug.LogError(
                $"{nameof(SceneRuntime)} missing {nameof(ProjectContext)}. Scene '{GetSceneLabel(gameObject.scene)}' cannot install scene-level dependencies.",
                this);

            return false;
        }

        if (installAttempted)
            return false;

        installAttempted = true;

        context.ResolveReferences();
        ValidateSceneKind(context);

        if (features == null || features.Length == 0)
        {
            Debug.LogError(
                $"{nameof(SceneRuntime)} on scene '{GetSceneLabel(gameObject.scene)}' has no feature references.",
                this);

            return false;
        }

        bool allFeaturesInstalled = true;

        for (int i = 0; i < features.Length; i++)
        {
            SceneRuntimeFeature feature = features[i];

            if (feature == null)
            {
                Debug.LogError(
                    $"Feature reference is null in {nameof(SceneRuntime)} on scene '{GetSceneLabel(gameObject.scene)}' at index {i}.",
                    this);

                allFeaturesInstalled = false;
                continue;
            }

            bool featureInstalled = feature.Install(context);

            if (!featureInstalled)
            {
                Debug.LogError(
                    $"{nameof(SceneRuntimeFeature)} failed install: {feature.GetType().Name} on scene '{GetSceneLabel(gameObject.scene)}'.",
                    feature);

                allFeaturesInstalled = false;
            }
        }

        installed = allFeaturesInstalled;
        return installed;
    }

    public static bool InstallActiveScene(ProjectContext context)
    {
        return InstallScene(SceneManager.GetActiveScene(), context);
    }

    public static bool InstallScene(Scene scene, ProjectContext context)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return false;

        if (context == null)
        {
            Debug.LogError(
                $"{nameof(SceneRuntime)} missing {nameof(ProjectContext)} while installing scene '{GetSceneLabel(scene)}'.");

            return false;
        }

        bool foundRuntime = false;
        bool installedRuntime = false;
        GameObject[] roots = scene.GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            SceneRuntime[] runtimes = roots[i].GetComponentsInChildren<SceneRuntime>(true);

            for (int runtimeIndex = 0; runtimeIndex < runtimes.Length; runtimeIndex++)
            {
                foundRuntime = true;

                if (runtimes[runtimeIndex].Install(context))
                    installedRuntime = true;
            }
        }

        ProjectSceneKind sceneKind = context.GetSceneKind(scene.name, scene.path);

        if (!foundRuntime &&
            sceneKind != ProjectSceneKind.Unknown &&
            sceneKind != context.GetBootstrapSceneKind())
        {
            Debug.LogWarning($"Scene '{scene.name}' has no {nameof(SceneRuntime)}. Scene-level dependencies were not installed.");
        }

        if (foundRuntime && !installedRuntime)
            Debug.LogError($"Scene '{scene.name}' has {nameof(SceneRuntime)}, but scene-level dependencies were not installed successfully.");

        return installedRuntime;
    }

    private void ValidateSceneKind(ProjectContext context)
    {
        if (sceneKind == ProjectSceneKind.Unknown)
            return;

        ProjectSceneKind configuredKind = context.GetSceneKind(gameObject.scene.name, gameObject.scene.path);

        if (configuredKind == sceneKind)
            return;

        if (configuredKind == ProjectSceneKind.Unknown)
        {
            Debug.LogWarning(
                $"{nameof(SceneRuntime)} on scene '{gameObject.scene.name}' is marked as {sceneKind}, " +
                $"but the scene is not registered in {nameof(ProjectSettings)}.",
                this);
            return;
        }

        Debug.LogWarning(
            $"{nameof(SceneRuntime)} on scene '{gameObject.scene.name}' is marked as {sceneKind}, " +
            $"but project settings identify it as {configuredKind}.",
            this);
    }

    private static string GetSceneLabel(Scene scene)
    {
        if (!string.IsNullOrWhiteSpace(scene.path))
            return scene.path;

        if (!string.IsNullOrWhiteSpace(scene.name))
            return scene.name;

        return "Unknown";
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        features = GetComponents<SceneRuntimeFeature>();
    }
#endif
}