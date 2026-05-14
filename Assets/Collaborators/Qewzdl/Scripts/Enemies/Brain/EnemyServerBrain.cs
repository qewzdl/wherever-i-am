using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class EnemyServerBrain
{
    private readonly EnemyConfig config;
    private readonly EnemyNavigator navigator;
    private readonly EnemyTargetDetector targetDetector;
    private readonly EnemyAttackController attackController;
    private readonly Action<EnemyState> setState;
    private readonly Action<EnemyTargetIdentity> setTargetIdentity;
    private readonly EnemyInvestigationDebugData investigationDebugData;

    private readonly EnemyTargetMemory targetMemory = new();
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
        EnemyPatrolController patrolController,
        EnemyAttackController attackController,
        EnemyInvestigationDebugData investigationDebugData,
        Action<EnemyState> setState,
        Action<EnemyTargetIdentity> setTargetIdentity
    )
    {
        this.config = config;
        this.navigator = navigator;
        this.targetDetector = targetDetector;
        this.attackController = attackController;
        this.investigationDebugData = investigationDebugData;
        this.setState = setState;
        this.setTargetIdentity = setTargetIdentity;

        context = new EnemyBrainContext(
            config,
            navigator,
            targetDetector,
            patrolController,
            attackController,
            targetMemory,
            investigationDebugData,
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

        attackController.Tick(deltaTime);
        TickVisualTargetMemory(deltaTime);
        TickTargetRefresh(deltaTime);
        currentHandler?.Tick(deltaTime);
    }

    public void Dispose()
    {
        currentHandler?.Exit();
        currentHandler = null;

        targetMemory.ClearAll();
        investigationDebugData?.Clear();
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

    private void TickVisualTargetMemory(float deltaTime)
    {
        if (!targetMemory.IsUsingVisualMemory)
        {
            return;
        }

        bool stillHasTarget = targetMemory.TickVisualMemory(deltaTime);

        if (!stillHasTarget)
        {
            SyncTarget();

            if (currentState == EnemyState.Chase || currentState == EnemyState.Attack)
            {
                ChangeState(EnemyState.Investigate);
            }
        }
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
        if (targetDetector == null)
        {
            HandleNoStimulus();
            return;
        }

        if (!targetDetector.TryFindBestStimulus(config, out EnemyPerceptionStimulus stimulus))
        {
            HandleNoStimulus();
            return;
        }

        if (stimulus.IsConfirmedTarget && stimulus.HasTarget)
        {
            ApplyConfirmedTargetStimulus(stimulus);
            return;
        }

        ApplySuspiciousStimulus(stimulus);
    }

    private void ApplyConfirmedTargetStimulus(EnemyPerceptionStimulus stimulus)
    {
        if (targetMemory.HasTarget && targetMemory.CurrentTarget == stimulus.Target)
        {
            targetMemory.RefreshConfirmedTarget(stimulus.Position);
        }
        else
        {
            targetMemory.SetTarget(stimulus.Target, stimulus.Position);
        }

        targetMemory.ClearSecondarySuspiciousPosition();
        SyncTarget();

        if (currentState != EnemyState.Chase && currentState != EnemyState.Attack)
        {
            ChangeState(EnemyState.Chase);
        }
    }

    private void ApplySuspiciousStimulus(EnemyPerceptionStimulus stimulus)
    {
        if (!stimulus.HasStimulus)
        {
            return;
        }

        if (IsPursuingConfirmedTarget())
        {
            targetMemory.RememberSecondarySuspiciousPosition(stimulus.Position);
            return;
        }

        if (targetMemory.HasTarget)
        {
            targetMemory.StartVisualMemoryGracePeriod(config.visualTargetMemoryDuration);
            return;
        }

        targetMemory.RememberPosition(stimulus.Position);

        if (currentState != EnemyState.Investigate)
        {
            ChangeState(EnemyState.Investigate);
        }
    }

    private bool IsPursuingConfirmedTarget()
    {
        return targetMemory.HasTarget &&
               targetMemory.IsCurrentTargetValid &&
               (currentState == EnemyState.Chase || currentState == EnemyState.Attack);
    }

    private void HandleNoStimulus()
    {
        if (currentState == EnemyState.Chase || currentState == EnemyState.Attack)
        {
            if (targetMemory.HasTarget)
            {
                targetMemory.StartVisualMemoryGracePeriod(config.visualTargetMemoryDuration);
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
            SyncTarget();
        }
    }

    private void SyncTarget()
    {
        setTargetIdentity?.Invoke(targetMemory.CurrentTargetIdentity);
    }
}