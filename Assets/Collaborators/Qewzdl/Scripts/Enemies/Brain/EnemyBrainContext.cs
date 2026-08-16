using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class EnemyBrainContext
{
    private static int nextPerceptionId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticIds()
    {
        nextPerceptionId = 0;
    }

    private readonly Action<EnemyState> changeState;
    private readonly Action syncTarget;
    private readonly int perceptionId;

    public EnemyConfig Config { get; }
    public EnemyNavigator Navigator { get; }
    public EnemyTargetDetector TargetDetector { get; }
    public EnemyPatrolController PatrolController { get; }
    public EnemyAttackController AttackController { get; }
    public EnemyPostureController PostureController { get; }
    public EnemyBlackboard Blackboard { get; }

    // Plans routes with this enemy's own agent type, area mask, posture and
    // blockers, so a tactical destination that passes is one it can reach.
    public EnemyTacticalNavigationPlanner TacticalPlanner { get; }

    public EnemyStealthManeuver Maneuver => Blackboard.StealthManeuver;

    public EnemyEngagementTacticsRuntime EngagementTactics =>
        Blackboard.EngagementTactics;

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

        TacticalPlanner = new EnemyTacticalNavigationPlanner(navigator);

        perceptionId = ++nextPerceptionId;
    }

    // Whether anyone can see this enemy where it is standing.
    //
    // Goes through the shared scheduler rather than PlayerGazeNetwork so the
    // answer is reused for a fraction of a second. Four stealth states asked
    // this every frame, and each ask is up to three raycasts per player, so
    // the cost was enemies times players times three, every frame, for a
    // question whose answer does not change that fast.
    public bool IsSelfSeenByAnyone()
    {
        return GetWatchers().Count > 0;
    }

    // Everyone who can see this enemy right now, from the same cached sample
    // the bool above reads.
    //
    // The bool is not enough to plan with. An enemy noticed by player B while
    // it works on player A backed away from A, and in a shared room that is a
    // route towards B - it broke off, walked into the person who had spotted
    // it, and broke off again. Read within the tick that asks for it: the
    // scheduler refills the list on the next sample.
    public IReadOnlyList<PlayerWatcher> GetWatchers()
    {
        if (Navigator == null)
        {
            return EnemyServerPerceptionScheduler.NoWatchers;
        }

        return EnemyServerPerceptionScheduler.GetBodyWatchers(
            perceptionId,
            Navigator.Position,
            Navigator.BodyHeight,
            Config != null
                ? Config.stealthVisibilityRefreshInterval
                : EnemyServerPerceptionScheduler.DefaultGazeRefreshInterval
        );
    }

    // Every phase of the manoeuvre begins with the same question: is this
    // still a manoeuvre, against the same person, on evidence recent enough
    // to plan with? A false answer has already picked the way out.
    public bool TryContinueManeuver(out EnemyTargetObservation observation)
    {
        EnemyStealthManeuverStatus status = Maneuver.Evaluate(
            Time.time,
            Config != null ? Config.stealthObservationMaxAge : 10f,
            Config != null ? Config.stealthManeuverTimeout : 25f,
            out observation
        );

        switch (status)
        {
            case EnemyStealthManeuverStatus.Running:
                return true;

            // Sneaking has had its chance. Coming at them is the same answer
            // each phase's own timeout reaches, for the same reason.
            case EnemyStealthManeuverStatus.Expired:
                GiveUpOnStealth(EnemyStealthFailureReason.Timeout);
                return false;

            default:
                ChangeState(EnemyState.Investigate);
                return false;
        }
    }

    // Sneaking is off, and the chase that follows is not allowed to be talked
    // back into stalking by the next distance check. Every phase that used to
    // answer a dead end with a bare ChangeState(Chase) comes through here, so
    // the reason survives and the commitment is what ends the loop rather than
    // the enemy simply drifting far enough away to start it again.
    public void GiveUpOnStealth(EnemyStealthFailureReason reason)
    {
        EnemyStealthTacticsConfig tactics =
            Config != null ? Config.StealthTactics : null;

        EngagementTactics.CommitToAssault(
            reason,
            Time.time,
            tactics != null ? tactics.assaultCommitDuration : 10f,
            GetRelevantGazeTopologySignature()
        );

        ChangeState(EnemyState.Chase);
    }

    // A tactical change is local to the engagement. The raw gaze registry is
    // match-wide, so hashing it directly made an unrelated player walking in
    // another room release this enemy's Assault commitment.
    public int GetRelevantGazeTopologySignature()
    {
        Vector3 focusPosition = Navigator != null
            ? Navigator.Position
            : Vector3.zero;

        if (TargetMemory.HasTarget && TargetMemory.IsCurrentTargetValid)
        {
            focusPosition = GetTargetNavigationPosition(
                TargetMemory.CurrentTarget
            );
        }
        else if (Maneuver.Observation.IsValid)
        {
            focusPosition = Maneuver.Observation.Position;
        }
        else if (InvestigationMemory.HasLastKnownTargetPosition)
        {
            focusPosition = InvestigationMemory.LastKnownTargetPosition;
        }

        return GetRelevantGazeTopologySignature(focusPosition);
    }

    public int GetRelevantGazeTopologySignature(Vector3 focusPosition)
    {
        float relevanceRadius = Config != null
            ? Config.loseTargetDistance
            : 30f;

        return EnemyEngagementTacticsRuntime.GazeTopologySignature(
            focusPosition,
            relevanceRadius
        );
    }

    // Identifies this enemy to anything shared between enemies - the query
    // budgets and the claimed tactical spots.
    public int TacticalOwnerId =>
        Navigator != null ? Navigator.NavigationSchedulerId : perceptionId;

    public void ForgetPerceptionCache()
    {
        EnemyServerPerceptionScheduler.Forget(perceptionId);
        EnemyTacticalSlotRegistry.Release(TacticalOwnerId);

        if (Navigator != null)
        {
            EnemyServerPerceptionScheduler.Forget(
                Navigator.NavigationSchedulerId
            );
        }
    }

    public bool TryReservePlanningQueries(
        int maximumPathQueries,
        int maximumVisibilityQueries,
        out int grantedPathQueries,
        out int grantedVisibilityQueries
    )
    {
        if (Navigator == null)
        {
            grantedPathQueries = 0;
            grantedVisibilityQueries = 0;
            return false;
        }

        return EnemyServerPerceptionScheduler.TryReservePlanningQueries(
            Navigator.NavigationSchedulerId,
            maximumPathQueries,
            maximumVisibilityQueries,
            out grantedPathQueries,
            out grantedVisibilityQueries
        );
    }

    public void ChangeState(EnemyState nextState)
    {
        changeState?.Invoke(nextState);
    }

    public void SyncTarget()
    {
        syncTarget?.Invoke();
    }

    public bool TryMoveTo(Vector3 destination, float speed, bool allowPushThrough = false)
    {
        if (Navigator == null)
        {
            Blackboard.ClearCurrentDestination();
            return false;
        }

        bool moved = Navigator.TryMoveTo(destination, speed, allowPushThrough);

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
        EngagementTactics.Clear();
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
