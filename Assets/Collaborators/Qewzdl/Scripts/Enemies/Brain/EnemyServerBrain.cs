using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class EnemyServerBrain
{
    private readonly EnemyConfig config;
    private readonly EnemyNavigator navigator;
    private readonly EnemyTargetDetector targetDetector;
    private readonly bool usesTargetDetection;
    private readonly EnemyAttackController attackController;
    private readonly EnemyPostureController postureController;
    private readonly EnemyBlackboard blackboard;
    private readonly Action<EnemyState> setState;
    private readonly Action<EnemyTargetIdentity> setTargetIdentity;

    private readonly Dictionary<EnemyState, IEnemyStateHandler> stateHandlers = new();

    private EnemyBrainContext context;
    private IEnemyStateHandler currentHandler;
    private EnemyState currentState = EnemyState.Idle;
    private float targetRefreshTimer;

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

        this.config = config;
        this.navigator = navigator;
        this.targetDetector = targetDetector;
        this.usesTargetDetection = usesTargetDetection;
        this.attackController = attackController;
        this.postureController = postureController;
        this.blackboard = blackboard ?? new EnemyBlackboard();
        this.setState = setState;
        this.setTargetIdentity = setTargetIdentity;

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

        if (TickVisualTargetMemory(deltaTime))
        {
            currentHandler?.Tick(deltaTime);
            return;
        }

        if (usesTargetDetection)
        {
            TickTargetRefresh(deltaTime);
        }

        currentHandler?.Tick(deltaTime);
    }

    public void Dispose()
    {
        attackController?.Interrupt();

        currentHandler?.Exit();
        currentHandler = null;

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

    private bool TickVisualTargetMemory(float deltaTime)
    {
        EnemyPerceptionMemory perceptionMemory = blackboard.PerceptionMemory;

        if (!perceptionMemory.IsUsingVisualMemory)
        {
            return false;
        }

        bool stillHasTarget = perceptionMemory.TickVisualMemory(
            deltaTime,
            out EnemyTarget rememberedTarget,
            out Vector3 rememberedPosition,
            out bool hasRememberedPosition
        );

        if (hasRememberedPosition)
        {
            blackboard.InvestigationMemory.RememberLastKnownTargetPosition(rememberedPosition);
        }

        if (stillHasTarget && rememberedTarget != null)
        {
            blackboard.SetCurrentStimulus(
                EnemyPerceptionStimulus.ForConfirmedTarget(
                    rememberedTarget,
                    rememberedPosition,
                    1f,
                    EnemyPerceptionSource.Vision
                ),
                Time.time
            );

            return false;
        }

        blackboard.TargetMemory.ForgetCurrentTargetButKeepLastKnownPosition();
        blackboard.ClearCurrentStimulus();
        SyncTarget();

        if (currentState == EnemyState.Chase || currentState == EnemyState.Attack)
        {
            ChangeState(EnemyState.Investigate);
            return true;
        }

        return false;
    }

    private void TickTargetRefresh(float deltaTime)
    {
        targetRefreshTimer -= deltaTime;

        if (targetRefreshTimer > 0f)
        {
            return;
        }

        targetRefreshTimer = Mathf.Max(0.05f, config.targetRefreshInterval);
        RefreshTarget();
    }

    private void RefreshTarget()
    {
        if (!targetDetector.TryResolveBestStimulus(
                config,
                blackboard,
                currentState,
                out EnemyStimulusResolution resolution
            ))
        {
            HandleNoStimulus();
            return;
        }

        ApplyStimulusResolution(resolution);
    }

    private void ApplyStimulusResolution(EnemyStimulusResolution resolution)
    {
        if (!resolution.HasResolution)
        {
            HandleNoStimulus();
            return;
        }

        blackboard.SetCurrentStimulus(resolution.PrimaryStimulus, Time.time);

        switch (resolution.Action)
        {
            case EnemyStimulusResolutionAction.ChaseConfirmedTarget:
                ApplyConfirmedTargetStimulus(resolution.PrimaryStimulus);

                if (resolution.HasSecondaryStimulus)
                {
                    RememberSecondarySuspiciousStimulus(resolution.SecondaryStimulus);
                }

                break;

            case EnemyStimulusResolutionAction.InvestigateSuspiciousPosition:
                ApplySuspiciousStimulus(
                    resolution.PrimaryStimulus,
                    resolution.ShouldClearCurrentTarget
                );

                break;

            case EnemyStimulusResolutionAction.RememberSecondarySuspicion:
                RememberSecondarySuspiciousStimulus(resolution.PrimaryStimulus);
                break;

            default:
                HandleNoStimulus();
                break;
        }
    }

    private void RememberSecondarySuspiciousStimulus(EnemyPerceptionStimulus stimulus)
    {
        if (!stimulus.HasStimulus)
        {
            return;
        }

        blackboard.InvestigationMemory.RememberSuspiciousPosition(stimulus.Position);
    }

    private void ApplyConfirmedTargetStimulus(EnemyPerceptionStimulus stimulus)
    {
        EnemyTargetMemory targetMemory = blackboard.TargetMemory;
        EnemyPerceptionMemory perceptionMemory = blackboard.PerceptionMemory;
        EnemyInvestigationMemory investigationMemory = blackboard.InvestigationMemory;

        if (targetMemory.HasTarget && targetMemory.CurrentTarget == stimulus.Target)
        {
            targetMemory.RefreshConfirmedTarget(stimulus.Target);
        }
        else
        {
            targetMemory.SetTarget(stimulus.Target);
        }

        investigationMemory.RememberLastKnownTargetPosition(stimulus.Position);
        investigationMemory.ClearSuspiciousPosition();

        perceptionMemory.CancelVisualMemory();

        SyncTarget();

        if (currentState != EnemyState.Chase && currentState != EnemyState.Attack)
        {
            ChangeState(EnemyState.Chase);
        }
    }

    private void ApplySuspiciousStimulus(
        EnemyPerceptionStimulus stimulus,
        bool forceInvestigate = false
    )
    {
        if (!stimulus.HasStimulus)
        {
            return;
        }

        EnemyTargetMemory targetMemory = blackboard.TargetMemory;
        EnemyPerceptionMemory perceptionMemory = blackboard.PerceptionMemory;
        EnemyInvestigationMemory investigationMemory = blackboard.InvestigationMemory;

        if (!forceInvestigate && IsPursuingConfirmedTarget())
        {
            investigationMemory.RememberSuspiciousPosition(stimulus.Position);
            return;
        }

        if (!forceInvestigate && targetMemory.HasTarget)
        {
            perceptionMemory.TryStartVisualMemoryGracePeriod(
                targetMemory.CurrentTarget,
                config.visualTargetMemoryDuration
            );

            return;
        }

        if (forceInvestigate && targetMemory.HasTarget)
        {
            targetMemory.ForgetCurrentTargetButKeepLastKnownPosition();
            perceptionMemory.CancelVisualMemory();
            SyncTarget();
        }

        investigationMemory.RememberLastKnownTargetPosition(stimulus.Position);

        if (currentState != EnemyState.Investigate)
        {
            ChangeState(EnemyState.Investigate);
        }
    }

    private bool IsPursuingConfirmedTarget()
    {
        EnemyTargetMemory targetMemory = blackboard.TargetMemory;

        return targetMemory.HasTarget &&
               targetMemory.IsCurrentTargetValid &&
               (currentState == EnemyState.Chase || currentState == EnemyState.Attack);
    }

    private void HandleNoStimulus()
    {
        blackboard.ClearCurrentStimulus();

        EnemyTargetMemory targetMemory = blackboard.TargetMemory;
        EnemyPerceptionMemory perceptionMemory = blackboard.PerceptionMemory;

        if (currentState == EnemyState.Chase || currentState == EnemyState.Attack)
        {
            if (targetMemory.HasTarget)
            {
                perceptionMemory.TryStartVisualMemoryGracePeriod(
                    targetMemory.CurrentTarget,
                    config.visualTargetMemoryDuration
                );
            }

            return;
        }

        if (currentState == EnemyState.Investigate)
        {
            return;
        }

        if (targetMemory.HasTarget || targetMemory.CurrentTargetIdentity.HasTarget)
        {
            targetMemory.ClearAll();
            perceptionMemory.ClearAll();
            blackboard.InvestigationMemory.ClearAll();

            SyncTarget();
        }
    }

    private void SyncTarget()
    {
        setTargetIdentity?.Invoke(blackboard.TargetMemory.CurrentTargetIdentity);
    }
}