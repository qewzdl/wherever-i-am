using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class ProjectSceneRegistry : MonoBehaviour, IProjectSceneRegistry
{
    [Header("Configuration")]
    [SerializeField] private ProjectSettings settings;
    [SerializeField] private ProjectSceneFlow sceneFlow;

    private bool referencesValidated;

    public ProjectSettings Settings => settings;
    public ProjectSceneFlow SceneFlow => sceneFlow;

    private void Awake()
    {
        ValidateReferencesOnce();
    }

    public string GetSceneName(ProjectSceneKind sceneKind)
    {
        if (TryGetScene(sceneKind, out ProjectSceneDefinition scene))
            return scene.SceneName;

        Debug.LogError($"Scene name is not configured for {sceneKind}.", this);
        return string.Empty;
    }

    public string GetScenePath(ProjectSceneKind sceneKind)
    {
        if (TryGetScene(sceneKind, out ProjectSceneDefinition scene))
            return scene.ScenePath;

        Debug.LogError($"Scene path is not configured for {sceneKind}.", this);
        return string.Empty;
    }

    public ProjectSceneKind GetActiveSceneKind()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        return GetSceneKind(activeScene.name, activeScene.path);
    }

    public ProjectSceneKind GetSceneKind(string sceneName)
    {
        return GetSceneKind(sceneName, string.Empty);
    }

    public ProjectSceneKind GetSceneKind(string sceneName, string scenePath)
    {
        ValidateReferencesOnce();

        if (settings == null)
            return ProjectSceneKind.Unknown;

        return settings.GetSceneKind(sceneName, scenePath);
    }

    public bool IsScene(ProjectSceneKind sceneKind, string sceneName)
    {
        return GetSceneKind(sceneName) == sceneKind;
    }

    public ProjectSceneKind GetBootstrapSceneKind()
    {
        ValidateReferencesOnce();

        if (settings == null)
            return ProjectSceneKind.Unknown;

        return settings.BootstrapScene;
    }

    public ProjectSceneKind GetDefaultStartupScene()
    {
        ValidateReferencesOnce();

        if (settings == null)
            return ProjectSceneKind.Unknown;

        return settings.DefaultStartupScene;
    }

    public GameState GetStateForScene(ProjectSceneKind sceneKind)
    {
        if (TryGetScene(sceneKind, out ProjectSceneDefinition scene))
            return scene.State;

        Debug.LogError($"Scene state is not configured for {sceneKind}.", this);
        return GameState.Error;
    }

    public bool TryGetScene(ProjectSceneKind sceneKind, out ProjectSceneDefinition scene)
    {
        ValidateReferencesOnce();

        if (settings == null)
        {
            scene = default;
            return false;
        }

        return settings.TryGetScene(sceneKind, out scene);
    }

    private void ValidateReferencesOnce()
    {
        if (referencesValidated)
            return;

        referencesValidated = true;

        ValidateRequiredReference(settings, nameof(settings));
        ValidateRequiredReference(sceneFlow, nameof(sceneFlow));
    }

    private void ValidateRequiredReference(Object reference, string fieldName)
    {
        if (reference != null)
            return;

        Debug.LogError($"{nameof(ProjectSceneRegistry)} is missing '{fieldName}'.", this);
    }
}
