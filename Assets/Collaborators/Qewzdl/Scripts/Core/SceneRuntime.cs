using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-900)]
[DisallowMultipleComponent]
public sealed class SceneRuntime : MonoBehaviour
{
    [SerializeField] private ProjectSceneKind sceneKind = ProjectSceneKind.Unknown;
    [SerializeField] private bool installOnAwake = true;
    [SerializeField] private SceneRuntimeFeature[] features;

    private bool installed;

    public ProjectSceneKind SceneKind => sceneKind;

    private void Awake()
    {
        if (installOnAwake)
            Install(ProjectContext.Instance);
    }

    public void Install(ProjectContext context)
    {
        if (installed)
            return;

        if (context == null)
            return;

        context.ResolveReferences();
        ValidateSceneKind(context);

        SceneRuntimeFeature[] sceneFeatures = features == null || features.Length == 0
            ? GetComponents<SceneRuntimeFeature>()
            : features;

        for (int i = 0; i < sceneFeatures.Length; i++)
        {
            SceneRuntimeFeature feature = sceneFeatures[i];

            if (feature == null)
                continue;

            feature.Install(context);
        }

        installed = true;
    }

    public static bool InstallActiveScene(ProjectContext context)
    {
        return InstallScene(SceneManager.GetActiveScene(), context);
    }

    public static bool InstallScene(Scene scene, ProjectContext context)
    {
        if (!scene.IsValid() || !scene.isLoaded || context == null)
            return false;

        bool installedRuntime = false;
        GameObject[] roots = scene.GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            SceneRuntime[] runtimes = roots[i].GetComponentsInChildren<SceneRuntime>(true);

            for (int runtimeIndex = 0; runtimeIndex < runtimes.Length; runtimeIndex++)
            {
                runtimes[runtimeIndex].Install(context);
                installedRuntime = true;
            }
        }

        ProjectSceneKind sceneKind = context.GetSceneKind(scene.name, scene.path);

        if (!installedRuntime &&
            sceneKind != ProjectSceneKind.Unknown &&
            sceneKind != context.GetBootstrapSceneKind())
        {
            Debug.LogWarning($"Scene '{scene.name}' has no {nameof(SceneRuntime)}. Scene-level dependencies were not installed.");
        }

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

#if UNITY_EDITOR
    private void OnValidate()
    {
        features = GetComponents<SceneRuntimeFeature>();
    }
#endif
}
