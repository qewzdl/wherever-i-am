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

        navigator.TickNavigationGate();

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
        Register(new EnemyStalkState(context));
        Register(new EnemyRetreatState(context));
        Register(new EnemyFlankState(context));
        Register(new EnemyAmbushState(context));
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
                ApplyConfirmedTarget();
                break;

            case EnemyPerceptionDecisionType.SuspiciousPosition:
                ApplySuspiciousPosition();
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

    // Losing sight of the target normally means going to look for it. Not
    // while circling round or lying in wait: those states are out of view on
    // purpose, so they lose the target constantly by design, and pulling them
    // into a search every time it happens meant the enemy never once reached
    // the ambush.
    //
    // Both have their own timeouts, so nothing is left standing forever.
    private void ApplySuspiciousPosition()
    {
        if (EnemyStateRules.HandlesOwnSightLoss(currentState) ||
            currentState == EnemyState.Investigate)
        {
            return;
        }

        ChangeState(EnemyState.Investigate);
    }

    // Near targets are run at, far ones are stalked. Between the two
    // thresholds nothing changes, so a target drifting across a single line
    // cannot flip the enemy back and forth.
    private void ApplyConfirmedTarget()
    {
        // Chase is the only engaged state the fork may still change - the
        // rest run their own sequence and say when they are done.
        if (EnemyStateRules.IsEngagedWithTarget(currentState) &&
            currentState != EnemyState.Chase)
        {
            return;
        }

        // No usable target position to measure against yet. Chasing is what
        // this did before stalking existed, and refusing to act at all would
        // mean a confirmed target that the enemy notices and then ignores.
        if (!blackboard.TargetMemory.HasTarget ||
            !blackboard.TargetMemory.IsCurrentTargetValid)
        {
            if (currentState != EnemyState.Chase)
            {
                ChangeState(EnemyState.Chase);
            }

            return;
        }

        float distance = Vector3.Distance(
            context.Navigator.Position,
            context.GetTargetNavigationPosition(blackboard.TargetMemory.CurrentTarget)
        );

        float chaseDistance = context.Config.chaseWithoutStalkingDistance;
        float stalkDistance = context.Config.stalkInsteadOfChasingDistance;

        // Not committed to either yet - coming off patrol, idle or a search.
        // There is nothing to flip between, so the band must not stop it
        // deciding: a target first seen inside the band would otherwise be
        // noticed and then ignored.
        if (currentState != EnemyState.Chase)
        {
            ChangeState(distance >= (chaseDistance + stalkDistance) * 0.5f
                ? EnemyState.Stalk
                : EnemyState.Chase);

            return;
        }

        // Already chasing. Only the far edge of the band turns that into
        // stalking, so a target drifting around the threshold does not keep
        // changing the enemy's mind. The way back is Stalk's own business.
        if (distance >= stalkDistance)
        {
            ChangeState(EnemyState.Stalk);
        }
    }

    private void SyncTarget()
    {
        setTargetIdentity?.Invoke(blackboard.TargetMemory.CurrentTargetIdentity);
    }
}