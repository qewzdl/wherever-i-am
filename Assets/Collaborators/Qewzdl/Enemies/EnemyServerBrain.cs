using System;
using UnityEngine;

public sealed class EnemyServerBrain
{
    private readonly EnemyConfig config;
    private readonly EnemyNavigator navigator;
    private readonly EnemyTargetDetector targetDetector;
    private readonly EnemyPatrolController patrolController;
    private readonly EnemyAttackController attackController;
    private readonly Action<EnemyState> setState;
    private readonly Action<ulong> setTargetClientId;
    private readonly EnemyTargetMemory targetMemory = new();

    private EnemyState state = EnemyState.Idle;
    private float targetRefreshTimer;

    public bool HasStarted { get; private set; }

    public EnemyServerBrain(
        EnemyConfig config,
        EnemyNavigator navigator,
        EnemyTargetDetector targetDetector,
        EnemyPatrolController patrolController,
        EnemyAttackController attackController,
        Action<EnemyState> setState,
        Action<ulong> setTargetClientId
    )
    {
        this.config = config;
        this.navigator = navigator;
        this.targetDetector = targetDetector;
        this.patrolController = patrolController;
        this.attackController = attackController;
        this.setState = setState;
        this.setTargetClientId = setTargetClientId;
    }

    public void Start()
    {
        if (HasStarted)
        {
            return;
        }

        if (config == null || navigator == null || !navigator.TryEnsureOnNavMesh())
        {
            SetState(EnemyState.Idle);
            return;
        }

        HasStarted = true;

        if (patrolController != null && patrolController.HasRoute)
        {
            SetState(EnemyState.Patrol);
            patrolController.MoveToNextPoint();
        }
        else
        {
            SetState(EnemyState.Idle);
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
        TickTargetRefresh(deltaTime);

        switch (state)
        {
            case EnemyState.Idle:
                TickIdle();
                break;

            case EnemyState.Patrol:
                TickPatrol();
                break;

            case EnemyState.Chase:
                TickChase();
                break;

            case EnemyState.Attack:
                TickAttack();
                break;

            case EnemyState.Investigate:
                TickInvestigate();
                break;
        }
    }

    public void Dispose()
    {
        targetMemory.ClearAll();
        setTargetClientId?.Invoke(EnemyTargetMemory.NoTargetClientId);
        HasStarted = false;
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
        EnemyTarget bestTarget = targetDetector != null
            ? targetDetector.FindBestVisibleTarget(config)
            : null;

        if (bestTarget == null)
        {
            HandleNoVisibleTarget();
            return;
        }

        Vector3 targetPosition = GetTargetNavigationPosition(bestTarget);
        targetMemory.SetTarget(bestTarget, targetPosition);
        SyncTarget();
    }

    private void HandleNoVisibleTarget()
    {
        if (state == EnemyState.Chase || state == EnemyState.Attack)
        {
            targetMemory.ClearTargetOnly();
            SyncTarget();

            if (targetMemory.HasLastKnownTargetPosition)
            {
                StartInvestigate();
            }
            else
            {
                ReturnToDefaultBehaviour();
            }

            return;
        }

        if (state != EnemyState.Investigate)
        {
            targetMemory.ClearAll();
            SyncTarget();
        }
    }

    private void TickIdle()
    {
        if (targetMemory.HasTarget)
        {
            StartChase();
            return;
        }

        if (patrolController != null && patrolController.HasRoute)
        {
            SetState(EnemyState.Patrol);
            patrolController.MoveToNextPoint();
        }
    }

    private void TickPatrol()
    {
        if (targetMemory.HasTarget)
        {
            StartChase();
            return;
        }

        if (patrolController == null || !patrolController.HasRoute)
        {
            SetState(EnemyState.Idle);
            navigator.ResetPath();
            return;
        }

        if (patrolController.HasReachedCurrentPoint())
        {
            patrolController.MoveToNextPoint();
        }
    }

    private void TickChase()
    {
        if (!targetMemory.IsCurrentTargetValid)
        {
            targetMemory.ClearAll();
            SyncTarget();
            ReturnToDefaultBehaviour();
            return;
        }

        Vector3 targetPosition = GetTargetNavigationPosition(targetMemory.CurrentTarget);
        float distanceToTarget = Vector3.Distance(navigator.Position, targetPosition);

        if (distanceToTarget > config.loseTargetDistance)
        {
            targetMemory.ClearTargetOnly();
            SyncTarget();

            if (targetMemory.HasLastKnownTargetPosition)
            {
                StartInvestigate();
            }
            else
            {
                ReturnToDefaultBehaviour();
            }

            return;
        }

        if (distanceToTarget <= config.attackDistance)
        {
            StartAttack();
            return;
        }

        navigator.TryMoveTo(targetPosition, config.chaseSpeed);
    }

    private void TickAttack()
    {
        if (!targetMemory.IsCurrentTargetValid)
        {
            targetMemory.ClearAll();
            SyncTarget();
            ReturnToDefaultBehaviour();
            return;
        }

        Vector3 targetPosition = GetTargetNavigationPosition(targetMemory.CurrentTarget);
        float distanceToTarget = Vector3.Distance(navigator.Position, targetPosition);

        if (distanceToTarget > config.attackDistance)
        {
            StartChase();
            return;
        }

        navigator.Stop();

        attackController.TryAttack(
            targetMemory.CurrentTarget,
            config,
            navigator.Position,
            attackController
        );
    }

    private void TickInvestigate()
    {
        if (targetMemory.HasTarget)
        {
            StartChase();
            return;
        }

        if (!targetMemory.HasLastKnownTargetPosition)
        {
            ReturnToDefaultBehaviour();
            return;
        }

        navigator.TryMoveTo(targetMemory.LastKnownTargetPosition, config.chaseSpeed);

        if (navigator.HasReached(config.patrolPointReachDistance))
        {
            targetMemory.ClearAll();
            SyncTarget();
            ReturnToDefaultBehaviour();
        }
    }

    private void StartChase()
    {
        SetState(EnemyState.Chase);
    }

    private void StartAttack()
    {
        SetState(EnemyState.Attack);
        navigator.ResetPath();
        navigator.Stop();
    }

    private void StartInvestigate()
    {
        if (!targetMemory.HasLastKnownTargetPosition)
        {
            ReturnToDefaultBehaviour();
            return;
        }

        SetState(EnemyState.Investigate);

        if (!navigator.TryMoveTo(targetMemory.LastKnownTargetPosition, config.chaseSpeed))
        {
            targetMemory.ClearAll();
            SyncTarget();
            ReturnToDefaultBehaviour();
        }
    }

    private void ReturnToDefaultBehaviour()
    {
        if (patrolController != null && patrolController.HasRoute)
        {
            SetState(EnemyState.Patrol);
            patrolController.MoveToNextPoint();
            return;
        }

        SetState(EnemyState.Idle);
        navigator.ResetPath();
    }

    private Vector3 GetTargetNavigationPosition(EnemyTarget target)
    {
        if (target == null)
        {
            return navigator.Position;
        }

        if (target.NetworkObject != null && target.NetworkObject.IsSpawned)
        {
            return target.NetworkObject.transform.position;
        }

        return target.transform.position;
    }

    private void SetState(EnemyState nextState)
    {
        if (state == nextState)
        {
            return;
        }

        state = nextState;
        setState?.Invoke(nextState);
    }

    private void SyncTarget()
    {
        setTargetClientId?.Invoke(targetMemory.CurrentTargetClientId);
    }
}