using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class EnemyServerBrain
{
    private readonly EnemyConfig config;
    private readonly EnemyNavigator navigator;
    private readonly EnemyAttackController attackController;
    private readonly EnemyPostureController postureController;
    private readonly EnemyBlackboard blackboard;
    private readonly EnemyPerceptionRuntime perceptionRuntime;
    private readonly Action<EnemyState> setState;
    private readonly Action<EnemyTargetIdentity> setTargetIdentity;

    private readonly Dictionary<EnemyState, IEnemyStateHandler> stateHandlers = new();

    private EnemyBrainContext context;
    private IEnemyStateHandler currentHandler;
    private EnemyState currentState = EnemyState.Idle;

    public bool HasStarted { get; private set; }

    public EnemyServerBrain(
        EnemyConfig config,
        EnemyNavigator navigator,
        EnemyTargetDetector targetDetector,
        bool usesTargetDetection,
        EnemyPatrolController patrolController,
        EnemyAttackController attackController,
        EnemyPostureController postureController,
        EnemyBlackboard blackboard,
        Action<EnemyState> setState,
        Action<EnemyTargetIdentity> setTargetIdentity
    )
    {
        if (usesTargetDetection && targetDetector == null)
        {
            throw new ArgumentNullException(
                nameof(targetDetector),
                $"{nameof(EnemyServerBrain)} requires {nameof(EnemyTargetDetector)} when target detection is enabled."
            );
        }

        this.blackboard = blackboard ?? throw new ArgumentNullException(
            nameof(blackboard),
            $"{nameof(EnemyServerBrain)} requires non-null {nameof(EnemyBlackboard)}."
        );

        this.config = config;
        this.navigator = navigator;
        this.attackController = attackController;
        this.postureController = postureController;
        this.setState = setState;
        this.setTargetIdentity = setTargetIdentity;

        perceptionRuntime = new EnemyPerceptionRuntime(
            config,
            targetDetector,
            usesTargetDetection,
            this.blackboard,
            setTargetIdentity
        );

        context = new EnemyBrainContext(
            config,
            navigator,
            targetDetector,
            patrolController,
            attackController,
            postureController,
            this.blackboard,
            ChangeState,
            SyncTarget
        );

        RegisterStateHandlers();
    }

    public void Start()
    {
        if (HasStarted)
        {
            return;
        }

        if (config == null || navigator == null || !navigator.TryEnsureOnNavMesh())
        {
            ChangeState(EnemyState.Idle);
            return;
        }

        HasStarted = true;
        context.RefreshPosture();

        if (context.HasPatrolRoute)
        {
            ChangeState(EnemyState.Patrol);
        }
        else
        {
            ChangeState(EnemyState.Idle);
        }
    }

    public void Tick(float deltaTime)
    {
        if (!HasStarted || config == null || navigator == null)
        {
            return;
        }

        if (!navigator.TryEnsureOnNavMesh())
        {
            return;
        }

        context.RefreshPosture();

        attackController?.Tick(deltaTime, navigator.Position);

        EnemyPerceptionDecision perceptionDecision = perceptionRuntime.Tick(
            deltaTime,
            currentState
        );

        ApplyPerceptionDecision(perceptionDecision);

        currentHandler?.Tick(deltaTime);
    }

    public void Dispose()
    {
        attackController?.Interrupt();

        currentHandler?.Exit();
        currentHandler = null;

        perceptionRuntime.ResetRuntimeState();

        blackboard.ClearAll();
        SyncTarget();

        HasStarted = false;
    }

    private void RegisterStateHandlers()
    {
        Register(new EnemyIdleState(context));
        Register(new EnemyPatrolState(context));
        Register(new EnemyChaseState(context));
        Register(new EnemyAttackState(context));
        Register(new EnemyInvestigateState(context));
    }

    private void Register(IEnemyStateHandler handler)
    {
        if (handler == null)
        {
            return;
        }

        stateHandlers[handler.State] = handler;
    }

    private void ChangeState(EnemyState nextState)
    {
        if (currentHandler != null && currentState == nextState)
        {
            return;
        }

        if (!stateHandlers.TryGetValue(nextState, out IEnemyStateHandler nextHandler))
        {
            Debug.LogError($"{nameof(EnemyServerBrain)} has no handler for state {nextState}.");
            return;
        }

        currentHandler?.Exit();

        currentState = nextState;
        setState?.Invoke(nextState);

        currentHandler = nextHandler;
        currentHandler.Enter();
    }

    private void ApplyPerceptionDecision(EnemyPerceptionDecision decision)
    {
        if (!decision.HasDecision)
        {
            return;
        }

        switch (decision.Type)
        {
            case EnemyPerceptionDecisionType.ConfirmedTarget:
                if (currentState != EnemyState.Chase && currentState != EnemyState.Attack)
                {
                    ChangeState(EnemyState.Chase);
                }

                break;

            case EnemyPerceptionDecisionType.SuspiciousPosition:
                if (currentState != EnemyState.Investigate)
                {
                    ChangeState(EnemyState.Investigate);
                }

                break;

            case EnemyPerceptionDecisionType.None:
                break;

            default:
                Debug.LogError(
                    $"{nameof(EnemyServerBrain)} received unsupported perception decision {decision.Type}."
                );

                break;
        }
    }

    private void SyncTarget()
    {
        setTargetIdentity?.Invoke(blackboard.TargetMemory.CurrentTargetIdentity);
    }
}