using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

[DefaultExecutionOrder(-900)]
[DisallowMultipleComponent]
public sealed class SceneRuntime : MonoBehaviour
{
    [SerializeField] private ProjectSceneKind sceneKind = ProjectSceneKind.Unknown;
    [FormerlySerializedAs("installOnAwake")]
    [SerializeField] private bool installAutomatically = true;
    [SerializeField] private SceneRuntimeFeature[] features;

    private bool startCompleted;

    public ProjectSceneKind SceneKind => sceneKind;
    internal SceneRuntimeFeature[] Features => features;

    private void OnEnable()
    {
        if (!startCompleted || !installAutomatically)
            return;

        AppRuntime runtime = AppRuntime.Instance;

        if (runtime != null && runtime.IsRuntimeStarted)
            Install(ProjectContext.Instance);
    }

    private void Start()
    {
        startCompleted = true;

        if (installAutomatically)
            Install(ProjectContext.Instance);
    }

    private void OnDisable()
    {
        Uninstall();
    }

    private void OnDestroy()
    {
        Uninstall();
    }

    public bool Install(ProjectContext context)
    {
        return InstallScene(gameObject.scene, context);
    }

    public void Uninstall()
    {
        AppRuntime runtime = AppRuntime.Instance;

        if (runtime != null)
            runtime.UninstallSceneScope(gameObject.scene.handle);
    }

    public static bool InstallActiveScene(ProjectContext context)
    {
        return InstallScene(SceneManager.GetActiveScene(), context);
    }

    public static bool InstallScene(Scene scene, ProjectContext context)
    {
        AppRuntime runtime = AppRuntime.Instance;

        if (runtime == null)
        {
            Debug.LogError(
                $"Cannot install scene '{GetSceneLabel(scene)}' without {nameof(AppRuntime)}.");

            return false;
        }

        return runtime.InstallSceneScope(scene, context);
    }

    public static bool UninstallScene(Scene scene)
    {
        AppRuntime runtime = AppRuntime.Instance;
        return runtime != null && runtime.UninstallSceneScope(scene.handle);
    }

    internal void ValidateSceneKind(ProjectContext context)
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
