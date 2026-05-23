using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProjectSceneServiceComposer : MonoBehaviour
{
    [Header("Scene Services")]
    [SerializeField] private LocalSceneLoader localSceneLoader;
    [SerializeField] private NetworkSceneLoader networkSceneLoader;
    [SerializeField] private ProjectSceneNavigator sceneNavigator;
    [SerializeField] private ProjectSceneFlowService sceneFlowService;

    private bool compositionAttempted;
    private bool composed;

    public LocalSceneLoader LocalSceneLoader => localSceneLoader;
    public NetworkSceneLoader NetworkSceneLoader => networkSceneLoader;
    public ProjectSceneNavigator SceneNavigator => sceneNavigator;
    public ProjectSceneFlowService SceneFlowService => sceneFlowService;

    public bool Compose(ProjectContext projectContext)
    {
        if (composed)
            return true;

        if (compositionAttempted)
            return false;

        compositionAttempted = true;

        if (!HasRequiredReferences(projectContext))
            return false;

        localSceneLoader.Construct(projectContext);
        networkSceneLoader.Construct(projectContext);
        sceneNavigator.Construct(projectContext, localSceneLoader, networkSceneLoader);
        sceneFlowService.Construct(projectContext, sceneNavigator);

        composed = true;
        return true;
    }

    private bool HasRequiredReferences(ProjectContext projectContext)
    {
        bool valid = true;

        valid &= ValidateRequiredReference(projectContext, nameof(projectContext));
        valid &= ValidateRequiredReference(localSceneLoader, nameof(localSceneLoader));
        valid &= ValidateRequiredReference(networkSceneLoader, nameof(networkSceneLoader));
        valid &= ValidateRequiredReference(sceneNavigator, nameof(sceneNavigator));
        valid &= ValidateRequiredReference(sceneFlowService, nameof(sceneFlowService));

        return valid;
    }

    private bool ValidateRequiredReference(Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError($"{nameof(ProjectSceneServiceComposer)} is missing '{fieldName}'.", this);
        return false;
    }
}