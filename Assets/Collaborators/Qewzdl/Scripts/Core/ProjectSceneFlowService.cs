using System;
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
    [SerializeField] private MonoBehaviour[] completionGates;

    private ProjectNetworkSceneLoadCompletionTracker subscribedNetworkLoadCompletionTracker;
    private bool networkLoadCompletionSubscribed;

    public event Action<ProjectSceneKind> SceneLoadCompleted;
    public event Action<ProjectSceneKind> SceneLoadFailed;

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

        if (!ValidateCompletionGates(scene.Kind))
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

        BeginCompletion(scene.Kind, transition.ServerActionsAfterLoad, 0);
        return true;
    }

    private void HandleNetworkLoadCompleted(
        ProjectSceneKind loadedScene,
        ProjectSceneServerAction[] serverActionsAfterLoad)
    {
        if (!HasRequiredReferences())
            return;

        BeginCompletion(loadedScene, serverActionsAfterLoad, 0);
    }

    private void BeginCompletion(
        ProjectSceneKind loadedScene,
        ProjectSceneServerAction[] serverActionsAfterLoad,
        int startIndex)
    {
        if (completionGates != null)
        {
            for (int i = startIndex; i < completionGates.Length; i++)
            {
                MonoBehaviour behaviour = completionGates[i];

                if (behaviour is not IProjectSceneLoadCompletionGate gate ||
                    !gate.CanHandle(loadedScene))
                {
                    continue;
                }

                int nextIndex = i + 1;
                bool callbackInvoked = false;
                bool started = gate.BeginWait(
                    loadedScene,
                    success =>
                    {
                        callbackInvoked = true;

                        if (success)
                        {
                            BeginCompletion(loadedScene, serverActionsAfterLoad, nextIndex);
                            return;
                        }

                        FailCompletion(loadedScene, gate);
                    });

                if (!started && !callbackInvoked)
                    FailCompletion(loadedScene, gate);

                return;
            }
        }

        CompleteLoadNow(loadedScene, serverActionsAfterLoad);
    }

    private void CompleteLoadNow(
        ProjectSceneKind loadedScene,
        ProjectSceneServerAction[] serverActionsAfterLoad)
    {
        ApplyTargetState(loadedScene);
        postLoadActionRunner.Run(loadedScene, serverActionsAfterLoad);
        SceneLoadCompleted?.Invoke(loadedScene);
    }

    private bool ValidateCompletionGates(ProjectSceneKind sceneKind)
    {
        if (completionGates == null || completionGates.Length == 0)
            return true;

        for (int i = 0; i < completionGates.Length; i++)
        {
            MonoBehaviour behaviour = completionGates[i];

            if (behaviour == null)
            {
                Debug.LogError($"{nameof(ProjectSceneFlowService)} has an empty completion gate at index {i}.", this);
                return false;
            }

            if (behaviour is not IProjectSceneLoadCompletionGate gate)
            {
                Debug.LogError(
                    $"{behaviour.name} does not implement {nameof(IProjectSceneLoadCompletionGate)}.",
                    behaviour);

                return false;
            }

            if (gate.CanHandle(sceneKind) && !gate.Validate(sceneKind, out string error))
            {
                Debug.LogError(error, behaviour);
                return false;
            }
        }

        return true;
    }

    private void FailCompletion(ProjectSceneKind sceneKind, IProjectSceneLoadCompletionGate gate)
    {
        Debug.LogError(
            $"{nameof(ProjectSceneFlowService)} completion gate '{gate.GetType().Name}' failed for scene '{sceneKind}'.",
            this);

        stateMachine.ChangeState(GameState.Error);
        SceneLoadFailed?.Invoke(sceneKind);
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

    private bool ValidateRequiredReference(UnityEngine.Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError($"{nameof(ProjectSceneFlowService)} is missing '{fieldName}'.", this);
        return false;
    }
}
