using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProjectSceneServiceComposer : MonoBehaviour
{
    [Header("Scene Services")]
    [SerializeField] private LocalSceneLoader localSceneLoader;
    [SerializeField] private NetworkSceneLoader networkSceneLoader;
    [SerializeField] private ProjectSceneNavigator sceneNavigator;
    [SerializeField] private ProjectSceneFlowService sceneFlowService;

    [Header("Scene Flow Services")]
    [SerializeField] private ProjectSceneTransitionValidator sceneTransitionValidator;
    [SerializeField] private ProjectSceneLoadExecutor sceneLoadExecutor;
    [SerializeField] private ProjectNetworkSceneLoadCompletionTracker networkLoadCompletionTracker;
    [SerializeField] private ProjectScenePostLoadActionRunner postLoadActionRunner;

    private bool compositionAttempted;
    private bool composed;

    public LocalSceneLoader LocalSceneLoader => localSceneLoader;
    public NetworkSceneLoader NetworkSceneLoader => networkSceneLoader;
    public ProjectSceneNavigator SceneNavigator => sceneNavigator;
    public ProjectSceneFlowService SceneFlowService => sceneFlowService;
    public ProjectSceneTransitionValidator SceneTransitionValidator => sceneTransitionValidator;
    public ProjectSceneLoadExecutor SceneLoadExecutor => sceneLoadExecutor;
    public ProjectNetworkSceneLoadCompletionTracker NetworkLoadCompletionTracker => networkLoadCompletionTracker;
    public ProjectScenePostLoadActionRunner PostLoadActionRunner => postLoadActionRunner;

    public bool Compose(ProjectContext projectContext)
    {
        if (composed)
            return true;

        if (compositionAttempted)
            return false;

        compositionAttempted = true;

        if (!HasRequiredReferences(projectContext))
            return false;

        NetworkManager networkManager = projectContext.NetworkManager;
        GameStateMachine stateMachine = projectContext.StateMachine;

        localSceneLoader.Construct(projectContext);
        networkSceneLoader.Construct(projectContext);
        sceneNavigator.Construct(projectContext, localSceneLoader, networkSceneLoader);

        sceneTransitionValidator.Construct(projectContext, networkManager);
        sceneLoadExecutor.Construct(sceneNavigator, networkManager);
        networkLoadCompletionTracker.Construct(projectContext, networkManager);
        postLoadActionRunner.Construct(networkManager);

        sceneFlowService.Construct(
            projectContext,
            stateMachine,
            sceneTransitionValidator,
            sceneLoadExecutor,
            networkLoadCompletionTracker,
            postLoadActionRunner);

        composed = true;
        return true;
    }

    private bool HasRequiredReferences(ProjectContext projectContext)
    {
        bool valid = true;

        valid &= ValidateRequiredReference(projectContext, nameof(projectContext));

        if (projectContext != null)
        {
            valid &= ValidateRequiredReference(projectContext.NetworkManager, nameof(projectContext.NetworkManager));
            valid &= ValidateRequiredReference(projectContext.StateMachine, nameof(projectContext.StateMachine));
        }

        valid &= ValidateRequiredReference(localSceneLoader, nameof(localSceneLoader));
        valid &= ValidateRequiredReference(networkSceneLoader, nameof(networkSceneLoader));
        valid &= ValidateRequiredReference(sceneNavigator, nameof(sceneNavigator));
        valid &= ValidateRequiredReference(sceneFlowService, nameof(sceneFlowService));
        valid &= ValidateRequiredReference(sceneTransitionValidator, nameof(sceneTransitionValidator));
        valid &= ValidateRequiredReference(sceneLoadExecutor, nameof(sceneLoadExecutor));
        valid &= ValidateRequiredReference(networkLoadCompletionTracker, nameof(networkLoadCompletionTracker));
        valid &= ValidateRequiredReference(postLoadActionRunner, nameof(postLoadActionRunner));

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