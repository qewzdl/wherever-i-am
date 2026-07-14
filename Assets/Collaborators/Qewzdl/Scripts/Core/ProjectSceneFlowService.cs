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
    private long nextOperationId;
    private long activeOperationId;
    private bool hasActiveOperation;

    public bool HasPendingOperation => hasActiveOperation;

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
        CancelPendingOperations(ProjectOperationCancelReason.OwnerDisabled);
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

        if (hasActiveOperation)
        {
            Debug.LogError(
                $"Cannot load scene '{targetScene}' because another project scene operation is still pending.",
                this);

            return false;
        }

        if (!transitionValidator.TryGetTransition(
                targetScene,
                out ProjectSceneDefinition scene,
                out ProjectSceneTransitionDefinition transition))
            return false;

        if (!postLoadActionRunner.Validate(transition.ServerActionsAfterLoad))
            return false;

        if (!ValidateCompletionGates(scene.Kind))
            return false;

        long operationId = BeginOperation();

        bool shouldTrackNetworkCompletion = sceneLoadExecutor.ShouldTrackNetworkCompletion(transition);

        if (shouldTrackNetworkCompletion &&
            !networkLoadCompletionTracker.Track(
                operationId,
                scene.Kind,
                transition.ServerActionsAfterLoad))
        {
            EndOperation(operationId);
            return false;
        }

        stateMachine.ChangeState(transition.StateBeforeLoad);

        if (!sceneLoadExecutor.Load(scene, transition))
        {
            if (shouldTrackNetworkCompletion)
                networkLoadCompletionTracker.CancelPending();

            EndOperation(operationId);
            return false;
        }

        if (shouldTrackNetworkCompletion)
            return true;

        BeginCompletion(operationId, scene.Kind, transition.ServerActionsAfterLoad, 0);
        return true;
    }

    public void CancelPendingOperations(ProjectOperationCancelReason reason)
    {
        long cancelledOperationId = activeOperationId;
        hasActiveOperation = false;
        activeOperationId = 0;

        if (networkLoadCompletionTracker != null)
        {
            try
            {
                networkLoadCompletionTracker.CancelPending();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, networkLoadCompletionTracker);
            }
        }

        CancelCompletionGates(reason);

        if (cancelledOperationId != 0)
        {
            RuntimeLog.Info(
                $"Cancelled project scene operation {cancelledOperationId}. Reason: {reason}.",
                this);
        }
    }

    private void HandleNetworkLoadCompleted(
        long operationId,
        ProjectSceneKind loadedScene,
        ProjectSceneServerAction[] serverActionsAfterLoad)
    {
        if (!HasRequiredReferences())
            return;

        BeginCompletion(operationId, loadedScene, serverActionsAfterLoad, 0);
    }

    private void HandleNetworkLoadFailed(long operationId, ProjectSceneKind loadedScene)
    {
        FailCompletion(operationId, loadedScene, nameof(ProjectNetworkSceneLoadCompletionTracker));
    }

    private void BeginCompletion(
        long operationId,
        ProjectSceneKind loadedScene,
        ProjectSceneServerAction[] serverActionsAfterLoad,
        int startIndex)
    {
        if (!IsOperationActive(operationId))
            return;

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

                        if (!IsOperationActive(operationId))
                            return;

                        if (success)
                        {
                            BeginCompletion(
                                operationId,
                                loadedScene,
                                serverActionsAfterLoad,
                                nextIndex);
                            return;
                        }

                        FailCompletion(operationId, loadedScene, gate.GetType().Name);
                    });

                if (!started && !callbackInvoked)
                    FailCompletion(operationId, loadedScene, gate.GetType().Name);

                return;
            }
        }

        CompleteLoadNow(operationId, loadedScene, serverActionsAfterLoad);
    }

    private void CompleteLoadNow(
        long operationId,
        ProjectSceneKind loadedScene,
        ProjectSceneServerAction[] serverActionsAfterLoad)
    {
        if (!EndOperation(operationId))
            return;

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

    private void FailCompletion(
        long operationId,
        ProjectSceneKind sceneKind,
        string sourceName)
    {
        if (!EndOperation(operationId))
            return;

        networkLoadCompletionTracker.CancelPending();

        Debug.LogError(
            $"{nameof(ProjectSceneFlowService)} completion source '{sourceName}' failed for scene '{sceneKind}'.",
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
        subscribedNetworkLoadCompletionTracker.NetworkLoadFailed += HandleNetworkLoadFailed;
        networkLoadCompletionSubscribed = true;
    }

    private void UnsubscribeFromNetworkLoadCompletion()
    {
        if (!networkLoadCompletionSubscribed)
            return;

        if (subscribedNetworkLoadCompletionTracker != null)
        {
            subscribedNetworkLoadCompletionTracker.NetworkLoadCompleted -= HandleNetworkLoadCompleted;
            subscribedNetworkLoadCompletionTracker.NetworkLoadFailed -= HandleNetworkLoadFailed;
        }

        subscribedNetworkLoadCompletionTracker = null;
        networkLoadCompletionSubscribed = false;
    }

    private long BeginOperation()
    {
        nextOperationId++;

        if (nextOperationId == 0)
            nextOperationId++;

        activeOperationId = nextOperationId;
        hasActiveOperation = true;
        return activeOperationId;
    }

    private bool EndOperation(long operationId)
    {
        if (!IsOperationActive(operationId))
            return false;

        hasActiveOperation = false;
        activeOperationId = 0;
        return true;
    }

    private bool IsOperationActive(long operationId)
    {
        return hasActiveOperation &&
               operationId != 0 &&
               activeOperationId == operationId;
    }

    private void CancelCompletionGates(ProjectOperationCancelReason reason)
    {
        if (completionGates == null)
            return;

        for (int i = 0; i < completionGates.Length; i++)
        {
            if (completionGates[i] is IProjectSceneLoadCompletionGate gate)
            {
                try
                {
                    gate.CancelPending(reason);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, completionGates[i]);
                }
            }
        }
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
