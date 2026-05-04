using System;
using UnityEngine;

public sealed class EnemyBrainContext
{
    private readonly Action<EnemyState> changeState;
    private readonly Action syncTarget;

    public EnemyConfig Config { get; }
    public EnemyNavigator Navigator { get; }
    public EnemyTargetDetector TargetDetector { get; }
    public EnemyPatrolController PatrolController { get; }
    public EnemyAttackController AttackController { get; }
    public EnemyTargetMemory TargetMemory { get; }

    public bool HasPatrolRoute => PatrolController != null && PatrolController.HasRoute;

    public EnemyBrainContext(
        EnemyConfig config,
        EnemyNavigator navigator,
        EnemyTargetDetector targetDetector,
        EnemyPatrolController patrolController,
        EnemyAttackController attackController,
        EnemyTargetMemory targetMemory,
        Action<EnemyState> changeState,
        Action syncTarget
    )
    {
        Config = config;
        Navigator = navigator;
        TargetDetector = targetDetector;
        PatrolController = patrolController;
        AttackController = attackController;
        TargetMemory = targetMemory;
        this.changeState = changeState;
        this.syncTarget = syncTarget;
    }

    public void ChangeState(EnemyState nextState)
    {
        changeState?.Invoke(nextState);
    }

    public void SyncTarget()
    {
        syncTarget?.Invoke();
    }

    public void ClearTargetOnly()
    {
        TargetMemory.ClearTargetOnly();
        SyncTarget();
    }

    public void ClearAllTargetMemory()
    {
        TargetMemory.ClearAll();
        SyncTarget();
    }

    public void ReturnToDefaultBehaviour()
    {
        if (HasPatrolRoute)
        {
            ChangeState(EnemyState.Patrol);
            return;
        }

        ChangeState(EnemyState.Idle);
        Navigator.ResetPath();
    }

    public Vector3 GetTargetNavigationPosition(EnemyTarget target)
    {
        if (target == null)
        {
            return Navigator.Position;
        }

        if (target.NetworkObject != null && target.NetworkObject.IsSpawned)
        {
            return target.NetworkObject.transform.position;
        }

        return target.transform.position;
    }
}