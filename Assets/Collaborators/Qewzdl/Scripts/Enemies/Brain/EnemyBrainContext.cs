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
    public EnemyPostureController PostureController { get; }
    public EnemyBlackboard Blackboard { get; }

    public EnemyTargetMemory TargetMemory => Blackboard.TargetMemory;
    public EnemyPerceptionMemory PerceptionMemory => Blackboard.PerceptionMemory;
    public EnemyInvestigationMemory InvestigationMemory => Blackboard.InvestigationMemory;
    public EnemyInvestigationDebugData InvestigationDebugData => Blackboard.InvestigationDebugData;

    public bool HasPatrolRoute => PatrolController != null && PatrolController.HasRoute;

    public EnemyBrainContext(
        EnemyConfig config,
        EnemyNavigator navigator,
        EnemyTargetDetector targetDetector,
        EnemyPatrolController patrolController,
        EnemyAttackController attackController,
        EnemyPostureController postureController,
        EnemyBlackboard blackboard,
        Action<EnemyState> changeState,
        Action syncTarget
    )
    {
        Config = config;
        Navigator = navigator;
        TargetDetector = targetDetector;
        PatrolController = patrolController;
        AttackController = attackController;
        PostureController = postureController;
        Blackboard = blackboard ?? throw new ArgumentNullException(
            nameof(blackboard),
            $"{nameof(EnemyBrainContext)} requires non-null {nameof(EnemyBlackboard)}."
        );

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

    public bool TryMoveTo(
        Vector3 destination,
        float speed,
        bool allowBarrierPushThrough = true)
    {
        if (Navigator == null)
        {
            Blackboard.ClearCurrentDestination();
            return false;
        }

        bool moved = Navigator.TryMoveTo(
            destination,
            speed,
            allowBarrierPushThrough);

        if (moved)
        {
            Blackboard.SetCurrentDestination(destination);
        }
        else
        {
            Blackboard.ClearCurrentDestination();
        }

        RefreshPosture();
        return moved;
    }

    public void StopNavigation()
    {
        Navigator?.Stop();
        RefreshPosture();
    }

    public void ResetNavigationPath()
    {
        Navigator?.ResetPath();
        Blackboard.ClearCurrentDestination();
        RefreshPosture();
    }

    public void RefreshPosture()
    {
        if (PostureController == null)
        {
            return;
        }

        Blackboard.SetCurrentPosture(PostureController.CurrentPosture);
    }

    public void ClearTargetOnly()
    {
        TargetMemory.ClearTargetOnly();
        PerceptionMemory.CancelVisualMemory();
        SyncTarget();
    }

    public void ForgetCurrentTargetButKeepLastKnownPosition()
    {
        TargetMemory.ForgetCurrentTargetButKeepLastKnownPosition();
        PerceptionMemory.CancelVisualMemory();
        SyncTarget();
    }

    public void ClearAllTargetMemory()
    {
        TargetMemory.ClearAll();
        PerceptionMemory.ClearAll();
        InvestigationMemory.ClearAll();
        SyncTarget();
    }

    public void ReturnToDefaultBehaviour()
    {
        Blackboard.ClearCurrentInvestigationRoute();

        if (HasPatrolRoute)
        {
            ChangeState(EnemyState.Patrol);
            return;
        }

        ChangeState(EnemyState.Idle);
        ResetNavigationPath();
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