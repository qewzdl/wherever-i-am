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

    private bool composing;
    private bool compositionFailureLogged;
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

        if (composing)
            return false;

        composing = true;

        try
        {
            bool logErrors = !compositionFailureLogged;

            if (!HasRequiredReferences(projectContext, logErrors))
            {
                compositionFailureLogged = true;
                return false;
            }

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

            compositionFailureLogged = false;
            composed = true;
            return true;
        }
        finally
        {
            composing = false;
        }
    }

    private bool HasRequiredReferences(ProjectContext projectContext, bool logErrors)
    {
        bool valid = true;

        valid &= ValidateRequiredReference(projectContext, nameof(projectContext), logErrors);

        if (projectContext != null)
        {
            valid &= ValidateRequiredReference(projectContext.NetworkManager, nameof(projectContext.NetworkManager), logErrors);
            valid &= ValidateRequiredReference(projectContext.StateMachine, nameof(projectContext.StateMachine), logErrors);
        }

        valid &= ValidateRequiredReference(localSceneLoader, nameof(localSceneLoader), logErrors);
        valid &= ValidateRequiredReference(networkSceneLoader, nameof(networkSceneLoader), logErrors);
        valid &= ValidateRequiredReference(sceneNavigator, nameof(sceneNavigator), logErrors);
        valid &= ValidateRequiredReference(sceneFlowService, nameof(sceneFlowService), logErrors);
        valid &= ValidateRequiredReference(sceneTransitionValidator, nameof(sceneTransitionValidator), logErrors);
        valid &= ValidateRequiredReference(sceneLoadExecutor, nameof(sceneLoadExecutor), logErrors);
        valid &= ValidateRequiredReference(networkLoadCompletionTracker, nameof(networkLoadCompletionTracker), logErrors);
        valid &= ValidateRequiredReference(postLoadActionRunner, nameof(postLoadActionRunner), logErrors);

        return valid;
    }

    private bool ValidateRequiredReference(Object reference, string fieldName, bool logError)
    {
        if (reference != null)
            return true;

        if (logError)
            Debug.LogError($"{nameof(ProjectSceneServiceComposer)} is missing '{fieldName}'.", this);

        return false;
    }
}
