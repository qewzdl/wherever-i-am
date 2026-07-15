using UnityEngine;

[DefaultExecutionOrder(-900)]
[DisallowMultipleComponent]
public sealed class SceneRuntime : MonoBehaviour
{
    [SerializeField] private ProjectSceneKind sceneKind = ProjectSceneKind.Unknown;
    [SerializeField] private SceneRuntimeFeature[] features;

    public ProjectSceneKind SceneKind => sceneKind;
    internal SceneRuntimeFeature[] Features => features;

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

#if UNITY_EDITOR
    private void OnValidate()
    {
        features = GetComponents<SceneRuntimeFeature>();
    }
#endif
}
