using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProjectSceneFlowService : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ProjectContext projectContext;
    [SerializeField] private GameStateMachine stateMachine;
    [SerializeField] private ProjectSceneTransitionValidator transitionValidator;
    [SerializeField] private ProjectSceneLoadExecutor sceneLoadExecutor;
    [SerializeField] private ProjectNetworkSceneLoadCompletionTracker networkLoadCompletionTracker;
    [SerializeField] private ProjectScenePostLoadActionRunner postLoadActionRunner;

    private ProjectNetworkSceneLoadCompletionTracker subscribedNetworkLoadCompletionTracker;
    private bool networkLoadCompletionSubscribed;

    private void Awake()
    {
        HasRequiredReferences();
    }

    private void OnEnable()
    {
        SubscribeToNetworkLoadCompletion();
    }

    private void OnDisable()
    {
        UnsubscribeFromNetworkLoadCompletion();
    }

    public void Construct(
        ProjectContext context,
        GameStateMachine gameStateMachine,
        ProjectSceneTransitionValidator sceneTransitionValidator,
        ProjectSceneLoadExecutor loadExecutor,
        ProjectNetworkSceneLoadCompletionTracker loadCompletionTracker,
        ProjectScenePostLoadActionRunner actionRunner)
    {
        UnsubscribeFromNetworkLoadCompletion();

        projectContext = context;
        stateMachine = gameStateMachine;
        transitionValidator = sceneTransitionValidator;
        sceneLoadExecutor = loadExecutor;
        networkLoadCompletionTracker = loadCompletionTracker;
        postLoadActionRunner = actionRunner;

        if (isActiveAndEnabled)
            SubscribeToNetworkLoadCompletion();
    }

    public bool LoadScene(ProjectSceneKind targetScene)
    {
        if (!HasRequiredReferences())
            return false;

        if (!transitionValidator.TryGetTransition(
                targetScene,
                out ProjectSceneDefinition scene,
                out ProjectSceneTransitionDefinition transition))
            return false;

        if (!postLoadActionRunner.Validate(transition.ServerActionsAfterLoad))
            return false;

        bool shouldTrackNetworkCompletion = sceneLoadExecutor.ShouldTrackNetworkCompletion(transition);

        if (shouldTrackNetworkCompletion &&
            !networkLoadCompletionTracker.Track(scene.Kind, transition.ServerActionsAfterLoad))
            return false;

        stateMachine.ChangeState(transition.StateBeforeLoad);

        if (!sceneLoadExecutor.Load(scene, transition))
        {
            if (shouldTrackNetworkCompletion)
                networkLoadCompletionTracker.ClearPending();

            return false;
        }

        if (shouldTrackNetworkCompletion)
            return true;

        CompleteLoad(scene.Kind, transition.ServerActionsAfterLoad);
        return true;
    }

    private void HandleNetworkLoadCompleted(
        ProjectSceneKind loadedScene,
        ProjectSceneServerAction[] serverActionsAfterLoad)
    {
        if (!HasRequiredReferences())
            return;

        CompleteLoad(loadedScene, serverActionsAfterLoad);
    }

    private void CompleteLoad(
        ProjectSceneKind loadedScene,
        ProjectSceneServerAction[] serverActionsAfterLoad)
    {
        ApplyTargetState(loadedScene);
        postLoadActionRunner.Run(loadedScene, serverActionsAfterLoad);
    }

    private void ApplyTargetState(ProjectSceneKind sceneKind)
    {
        if (!projectContext.TryGetScene(sceneKind, out ProjectSceneDefinition scene))
        {
            Debug.LogError($"Scene state is not configured for {sceneKind}.", this);
            return;
        }

        stateMachine.ChangeState(scene.State);
    }

    private void SubscribeToNetworkLoadCompletion()
    {
        if (networkLoadCompletionSubscribed &&
            subscribedNetworkLoadCompletionTracker == networkLoadCompletionTracker)
            return;

        UnsubscribeFromNetworkLoadCompletion();

        if (networkLoadCompletionTracker == null)
            return;

        subscribedNetworkLoadCompletionTracker = networkLoadCompletionTracker;
        subscribedNetworkLoadCompletionTracker.NetworkLoadCompleted += HandleNetworkLoadCompleted;
        networkLoadCompletionSubscribed = true;
    }

    private void UnsubscribeFromNetworkLoadCompletion()
    {
        if (!networkLoadCompletionSubscribed)
            return;

        if (subscribedNetworkLoadCompletionTracker != null)
            subscribedNetworkLoadCompletionTracker.NetworkLoadCompleted -= HandleNetworkLoadCompleted;

        subscribedNetworkLoadCompletionTracker = null;
        networkLoadCompletionSubscribed = false;
    }

    private bool HasRequiredReferences()
    {
        bool valid = true;

        valid &= ValidateRequiredReference(projectContext, nameof(projectContext));
        valid &= ValidateRequiredReference(stateMachine, nameof(stateMachine));
        valid &= ValidateRequiredReference(transitionValidator, nameof(transitionValidator));
        valid &= ValidateRequiredReference(sceneLoadExecutor, nameof(sceneLoadExecutor));
        valid &= ValidateRequiredReference(networkLoadCompletionTracker, nameof(networkLoadCompletionTracker));
        valid &= ValidateRequiredReference(postLoadActionRunner, nameof(postLoadActionRunner));

        return valid;
    }

    private bool ValidateRequiredReference(Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError($"{nameof(ProjectSceneFlowService)} is missing '{fieldName}'.", this);
        return false;
    }
}