using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProjectSceneTransitionValidator : MonoBehaviour
{
    [SerializeField] private ProjectContext projectContext;
    [SerializeField] private NetworkManager networkManager;

    public void Construct(ProjectContext context, NetworkManager manager)
    {
        projectContext = context;
        networkManager = manager;
    }

    public void DisposeComposition()
    {
        projectContext = null;
        networkManager = null;
    }

    public bool TryGetTransition(
        ProjectSceneKind targetScene,
        out ProjectSceneDefinition scene,
        out ProjectSceneTransitionDefinition transition)
    {
        scene = default;
        transition = default;

        if (!HasRequiredReferences())
            return false;

        if (!projectContext.TryGetScene(targetScene, out scene))
        {
            Debug.LogError($"Scene is not configured for {targetScene}.", this);
            return false;
        }

        ProjectSceneKind currentScene = projectContext.GetActiveSceneKind();

        if (!projectContext.SceneFlow.TryGetTransition(currentScene, scene.Kind, out transition))
        {
            Debug.LogError($"Scene transition is not configured: {currentScene} -> {scene.Kind}.", this);
            return false;
        }

        return CanUseTransition(transition);
    }

    private bool CanUseTransition(ProjectSceneTransitionDefinition transition)
    {
        if (transition.Authority == ProjectSceneTransitionAuthority.ServerOnly &&
            !networkManager.IsServer)
        {
            Debug.LogWarning(
                $"Only server can execute scene transition {transition.FromScene} -> {transition.ToScene}.",
                this);

            return false;
        }

        if (!transition.RequiresActiveNetworkSession)
            return true;

        if (networkManager.IsListening)
            return true;

#if UNITY_EDITOR
        if (transition.AllowEditorDirectLoad)
            return true;
#endif

        Debug.LogError(
            $"Scene transition {transition.FromScene} -> {transition.ToScene} requires an active network session.",
            this);

        return false;
    }

    private bool HasRequiredReferences()
    {
        bool valid = true;

        valid &= ValidateRequiredReference(projectContext, nameof(projectContext));
        valid &= ValidateRequiredReference(networkManager, nameof(networkManager));

        if (projectContext != null)
            valid &= ValidateRequiredReference(projectContext.SceneFlow, nameof(projectContext.SceneFlow));

        return valid;
    }

    private bool ValidateRequiredReference(Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError($"{nameof(ProjectSceneTransitionValidator)} is missing '{fieldName}'.", this);
        return false;
    }
}
