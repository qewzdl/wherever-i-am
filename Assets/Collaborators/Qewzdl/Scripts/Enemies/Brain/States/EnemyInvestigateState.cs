using UnityEngine;
using UnityEngine.AI;

public sealed class EnemyInvestigateState : IEnemyStateHandler
{
    private enum InvestigationPhase
    {
        MovingToLastKnownPosition,
        CheckingHidingPlace,
        FollowingSearchRoute
    }

    private readonly EnemyBrainContext context;
    private readonly EnemyInvestigationSearchPlanner searchPlanner = new();

    private InvestigationPhase phase;

    private Vector3 investigationOrigin;
    private bool hasInvestigationOrigin;
    private Vector3 currentDestination;

    private int currentSearchPointIndex;
    private float repathTimer;
    private float dwellTimer;
    private float dwellDurationTotal;
    private Vector3 dwellArrivalForward;
    private bool isDwelling;

    private bool hasDestination;

    // Resolved on every Enter rather than in the constructor: the states are
    // built while the modules are being installed, and the module that provides
    // this one may not have had its turn yet. Null is the ordinary answer for
    // an enemy that was not given it.
    private EnemyHidingPlaceCheck hidingPlaceCheck;

    public EnemyState State => EnemyState.Investigate;

    public EnemyInvestigateState(EnemyBrainContext context)
    {
        this.context = context;
    }

    public void Enter()
    {
        ResetRuntimeState();

        context.Capabilities.TryGet(out hidingPlaceCheck);

        if (!TryResolveInvestigationOrigin(out investigationOrigin))
        {
            FinishInvestigation();
            return;
        }

        hasInvestigationOrigin = true;

        context.InvestigationDebugData?.Begin(investigationOrigin);

        phase = InvestigationPhase.MovingToLastKnownPosition;

        if (!TrySetDestination(investigationOrigin, context.Config.chaseSpeed))
        {
            TryMoveToSecondaryOrFinish();
        }
    }

    public void Tick(float deltaTime)
    {
        if (context.TargetMemory.HasTarget)
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        if (TryRestartOnNewerStimulus())
        {
            return;
        }

        repathTimer -= deltaTime;

        if (phase == InvestigationPhase.MovingToLastKnownPosition)
        {
            TickMovingToLastKnownPosition(deltaTime);
            return;
        }

        if (phase == InvestigationPhase.CheckingHidingPlace)
        {
            TickCheckingHidingPlace(deltaTime);
            return;
        }

        TickFollowingSearchRoute(deltaTime);
    }

    public void Exit()
    {
        context.InvestigationDebugData?.Finish();
        ResetRuntimeState();
    }

    private void TickMovingToLastKnownPosition(float deltaTime)
    {
        if (isDwelling)
        {
            if (TickPointDwell(deltaTime))
            {
                return;
            }

            StartHierarchicalSearch();
            return;
        }

        if (!hasDestination)
        {
            if (!TrySetDestination(investigationOrigin, context.Config.chaseSpeed))
            {
                TryMoveToSecondaryOrFinish();
            }

            return;
        }

        RepathToCurrentDestination(context.Config.chaseSpeed);

        if (!context.Navigator.HasReached(context.Config.investigationReachDistance))
        {
            return;
        }

        // A box she watched somebody climb into is not a place to look around
        // at, and this used to be the other way round.
        //
        // The dwell exists for a place where the target MIGHT be: she stops,
        // turns her head either way, and covers the corners the walk would
        // have dragged her cone past. When the origin is a hiding place she
        // saw the target get into, there is nothing to cover - she knows. So
        // the old order stood her in front of the box for the whole dwell,
        // a second and a half of scanning an empty room, before opening it.
        //
        // From a player's side that is the entire bug: watched from two metres
        // away, she walks over, stops, looks slowly left, looks slowly right,
        // and only then lifts the lid. It reads as her having lost you and
        // searching the room, because that is exactly the behaviour she is
        // performing.
        if (TryStartHidingPlaceCheck())
        {
            return;
        }

        if (TryBeginPointDwell())
        {
            return;
        }

        StartHierarchicalSearch();
    }

    // Standing still is the whole point. TickLookAround turns the body while
    // the enemy waits here, so a stationary stop covers the corners around
    // this point, where walking just drags the vision cone along the route.
    //
    // Has to be checked before RepathToCurrentDestination: the repath would
    // re-issue the destination and undo StopNavigation on its next interval.
    private bool TryBeginPointDwell()
    {
        float dwellDuration = context.Config.investigationPointDwellDuration;

        if (dwellDuration <= 0f)
        {
            return false;
        }

        isDwelling = true;
        dwellTimer = dwellDuration;
        dwellDurationTotal = dwellDuration;
        dwellArrivalForward = context.Navigator.transform.forward;
        context.StopNavigation();

        return true;
    }

    private bool TickPointDwell(float deltaTime)
    {
        dwellTimer -= Mathf.Max(0f, deltaTime);

        if (dwellTimer > 0f)
        {
            TickLookAround(deltaTime);
            return true;
        }

        isDwelling = false;
        dwellTimer = 0f;

        return false;
    }

    // First half of the dwell turns one way, second half the other. A turn that
    // does not fit in its half simply stops short - the enemy looks less far
    // rather than snapping around - so the angle and speed can be tuned against
    // the dwell without any of the three having to agree exactly.
    private void TickLookAround(float deltaTime)
    {
        float angle = context.Config.investigationLookAroundAngle;

        if (angle <= 0f || dwellDurationTotal <= 0f)
        {
            return;
        }

        bool isFirstHalf = dwellTimer > dwellDurationTotal * 0.5f;
        float targetYaw = isFirstHalf ? -angle : angle;
        Vector3 targetDirection =
            Quaternion.Euler(0f, targetYaw, 0f) * dwellArrivalForward;

        context.Navigator.FaceDirection(
            targetDirection,
            context.Config.investigationLookAroundSpeed,
            deltaTime
        );
    }

    // A stimulus that lands while the enemy is already investigating updates
    // the memory but cannot restart the state: ApplyPerceptionDecision only
    // calls ChangeState when the enemy is not already investigating, and
    // ChangeState returns early on a matching state, so Enter never runs again
    // and the plan built around the old origin stands. Notice the move here.
    private bool TryRestartOnNewerStimulus()
    {
        // Committed to opening a box. The check is short and bounded by its own
        // timer, and abandoning it would let any thrown object rescue a player
        // this enemy actually watched climb in.
        if (phase == InvestigationPhase.CheckingHidingPlace)
        {
            return false;
        }

        // A known hiding place is a stronger lead than a noise, and it is what
        // TryResolveInvestigationOrigin would pick anyway - restarting on it
        // would rebuild the same plan every tick.
        //
        // Asked of the capability rather than of the memory, because an enemy
        // that cannot check boxes has no business treating one as a lead. The
        // note is still written for her - perception does not know what she can
        // do - and without this she would refuse to follow a noise on the
        // strength of a box she will never open.
        if (hidingPlaceCheck != null && hidingPlaceCheck.HasRememberedPlace)
        {
            return false;
        }

        if (!context.InvestigationMemory.TryGetLastKnownTargetPosition(
                out Vector3 lastKnown))
        {
            return false;
        }

        // Inside the ring already being walked, the current plan covers it.
        // Restarting on every repeat of the same noise would thrash the route.
        float relevanceRadius = context.Config.investigationBranchRadius;

        if ((lastKnown - investigationOrigin).sqrMagnitude <=
            relevanceRadius * relevanceRadius)
        {
            return false;
        }

        Enter();
        return true;
    }

    // Walk up to the hiding place instead of stopping where the stimulus was:
    // the server only opens it from within EnemyInvestigationDistance, and the
    // investigation origin can sit a metre or two off the anchor.
    //
    // Everything about which box, how long to wait and how close to stand is
    // the capability's. What is left here is navigation, which is the state's
    // whichever way the enemy is configured.
    private bool TryStartHidingPlaceCheck()
    {
        if (hidingPlaceCheck == null ||
            !hidingPlaceCheck.TryBegin(out Vector3 approachPosition))
        {
            return false;
        }

        phase = InvestigationPhase.CheckingHidingPlace;

        if (!TrySetDestination(approachPosition, context.Config.chaseSpeed))
        {
            context.StopNavigation();
        }

        return true;
    }

    private void TickCheckingHidingPlace(float deltaTime)
    {
        // The null case cannot happen - the phase is only entered through
        // TryStartHidingPlaceCheck, which needs the capability - but a state
        // machine that would spin forever if it ever did is worth one branch.
        if (hidingPlaceCheck == null ||
            hidingPlaceCheck.Tick(deltaTime) ==
            EnemyHidingPlaceCheckStatus.Finished)
        {
            StartHierarchicalSearch();
            return;
        }

        RepathToCurrentDestination(context.Config.chaseSpeed);
    }

    private void StartHierarchicalSearch()
    {
        phase = InvestigationPhase.FollowingSearchRoute;
        currentSearchPointIndex = 0;

        context.InvestigationMemory.ClearLastKnownTargetPosition();

        searchPlanner.BuildHierarchicalSearchPlan(
            GetSearchOrigin(),
            context.Navigator.Position,
            context.Config.investigationBranchRadius,
            context.Config.investigationBranchPointCount,
            context.Config.investigationLeafRadius,
            context.Config.investigationLeafPointCountPerBranch,
            GetNavigationQueryFilter()
        );

        context.InvestigationDebugData?.SetSearchPoints(searchPlanner.Points);
        context.InvestigationDebugData?.SetBoundRoom(searchPlanner.OriginRoom);
        context.Blackboard.SetCurrentInvestigationRoute(searchPlanner.Points);

        if (searchPlanner.PointCount == 0)
        {
            TryMoveToSecondaryOrFinish();
            return;
        }

        MoveToNextSearchPointOrFinish();
    }

    // Where to centre the ring, which is not the same as where she walked to.
    //
    // She already knows which way the target was moving when she last saw
    // them: the observation carries a forward beside its position, and until
    // now only the flank planner ever read it. The search did not, so the ring
    // was built evenly around the spot where they vanished - and half of it
    // therefore covered ground behind her, which is the one place a running
    // target provably is not. She spent the dwell at those points looking at
    // an empty floor while the seconds the search is allowed ran out.
    //
    // Leaning the ring forward searches the half worth searching. The origin
    // she walked to is left alone: that is the last honest sighting, it is
    // what the hiding-place check and the debug overlay are about, and the
    // target may well still be standing on it.
    //
    // The push has to stay on floor she can walk. NavMesh.Raycast walks from
    // the origin towards the lead and stops at the first edge, so a target
    // last seen facing a wall shifts the ring as far as the wall and no
    // further. Without it the lead could land in the next room, and the
    // planner binds the whole route to whichever room its origin is in - she
    // would have gone off to search next door.
    private Vector3 GetSearchOrigin()
    {
        if (!context.TargetMemory.TryGetLastObservation(
                out EnemyTargetObservation observation) ||
            !EnemyInvestigationSearchPlanner.TryGetLeadOrigin(
                investigationOrigin,
                observation.Position,
                observation.Forward,
                context.Config.investigationLeadDistance,
                context.Config.investigationBranchRadius,
                out Vector3 leadOrigin))
        {
            return investigationOrigin;
        }

        if (!NavMesh.Raycast(
                investigationOrigin,
                leadOrigin,
                out NavMeshHit hit,
                GetNavigationQueryFilter()))
        {
            return leadOrigin;
        }

        // A query that could not start - the sighting was off the mesh -
        // reports a hit at infinity rather than an error. Anything past the
        // lead we asked for is not an answer, and NaN fails this too.
        return Vector3.Distance(hit.position, investigationOrigin) <=
               context.Config.investigationLeadDistance
            ? hit.position
            : investigationOrigin;
    }

    private void TickFollowingSearchRoute(float deltaTime)
    {
        if (isDwelling)
        {
            if (TickPointDwell(deltaTime))
            {
                return;
            }

            MoveToNextSearchPointOrFinish();
            return;
        }

        if (!hasDestination)
        {
            MoveToNextSearchPointOrFinish();
            return;
        }

        RepathToCurrentDestination(context.Config.investigationSearchSpeed);

        if (!context.Navigator.HasReached(context.Config.investigationReachDistance))
        {
            return;
        }

        if (TryBeginPointDwell())
        {
            return;
        }

        MoveToNextSearchPointOrFinish();
    }

    private void MoveToNextSearchPointOrFinish()
    {
        while (currentSearchPointIndex < searchPlanner.PointCount)
        {
            int routeIndex = currentSearchPointIndex;
            currentSearchPointIndex++;

            if (!searchPlanner.TryGetPoint(routeIndex, out Vector3 nextPoint))
            {
                continue;
            }

            if (!TrySetDestination(nextPoint, context.Config.investigationSearchSpeed))
            {
                continue;
            }

            context.InvestigationDebugData?.SetActiveRouteIndex(routeIndex);
            context.Blackboard.SetActiveInvestigationRouteIndex(routeIndex);

            return;
        }

        TryMoveToSecondaryOrFinish();
    }

    private void TryMoveToSecondaryOrFinish()
    {
        if (context.InvestigationMemory.PromoteSuspiciousPositionToLastKnown())
        {
            if (TryResolveInvestigationOrigin(out investigationOrigin))
            {
                hasInvestigationOrigin = true;

                context.InvestigationDebugData?.Begin(investigationOrigin);

                phase = InvestigationPhase.MovingToLastKnownPosition;
                currentSearchPointIndex = 0;

                searchPlanner.BuildHierarchicalSearchPlan(
                    investigationOrigin,
                    context.Navigator.Position,
                    context.Config.investigationBranchRadius,
                    context.Config.investigationBranchPointCount,
                    context.Config.investigationLeafRadius,
                    context.Config.investigationLeafPointCountPerBranch,
                    GetNavigationQueryFilter()
                );

                context.InvestigationDebugData?.SetSearchPoints(searchPlanner.Points);
                context.InvestigationDebugData?.SetBoundRoom(searchPlanner.OriginRoom);
                context.Blackboard.SetCurrentInvestigationRoute(searchPlanner.Points);

                if (!TrySetDestination(investigationOrigin, context.Config.chaseSpeed))
                {
                    FinishInvestigation();
                }

                return;
            }
        }

        FinishInvestigation();
    }

    // A remembered hiding place wins over the last known position, and it has
    // to. Visual memory hands out the target's LIVE position while it runs, so
    // a target that climbed into a box leaves its last known position inside
    // that box - inside the hole the box carves out of the NavMesh. Pathing
    // there fails, Enter gives up, and the enemy forgets a player it just
    // watched hide. The interaction anchor is reachable by construction.
    //
    // Safe to prefer because the reference now has exactly one writer: a
    // pursued target that vanished into a box while the enemy was watching.
    private bool TryResolveInvestigationOrigin(out Vector3 position)
    {
        if (hidingPlaceCheck != null &&
            hidingPlaceCheck.TryGetInvestigationOrigin(out position))
        {
            return true;
        }

        if (context.InvestigationMemory.TryGetLastKnownTargetPosition(out position))
        {
            return true;
        }

        if (context.InvestigationMemory.PromoteSuspiciousPositionToLastKnown())
        {
            return context.InvestigationMemory.TryGetLastKnownTargetPosition(out position);
        }

        position = default;
        return false;
    }

    // Investigation pushes through barricades like Chase does. Losing sight of
    // the target for a moment (the barricade itself blocks line of sight up
    // close) drops the enemy out of Chase, and without this the navigator
    // released its push-through holds, the items carved the route shut again,
    // and the enemy froze outside a sealed room until the target reappeared.
    private bool TrySetDestination(Vector3 destination, float speed)
    {
        currentDestination = destination;
        hasDestination = context.TryMoveTo(
            destination,
            speed,
            allowPushThrough: true);
        repathTimer = Mathf.Max(0.05f, context.Config.investigationRepathInterval);

        if (hasDestination)
        {
            context.InvestigationDebugData?.SetCurrentDestination(destination);
        }
        else
        {
            context.InvestigationDebugData?.ClearCurrentDestination();
        }

        return hasDestination;
    }

    private NavMeshQueryFilter GetNavigationQueryFilter()
    {
        EnemyPosture posture = context.PostureController != null
            ? context.PostureController.CurrentPosture
            : EnemyPosture.Standing;

        if (context.Navigator.TryGetNavigationQueryFilter(
                posture,
                out NavMeshQueryFilter filter))
        {
            return filter;
        }

        NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(0);
        return new NavMeshQueryFilter
        {
            agentTypeID = settings.agentTypeID,
            areaMask = NavMesh.AllAreas
        };
    }

    private void RepathToCurrentDestination(float speed)
    {
        if (!hasDestination || repathTimer > 0f)
        {
            return;
        }

        hasDestination = context.TryMoveTo(
            currentDestination,
            speed,
            allowPushThrough: true);
        repathTimer = Mathf.Max(0.05f, context.Config.investigationRepathInterval);

        if (hasDestination)
        {
            context.InvestigationDebugData?.SetCurrentDestination(currentDestination);
        }
        else
        {
            context.InvestigationDebugData?.ClearCurrentDestination();
        }
    }

    private void FinishInvestigation()
    {
        context.InvestigationDebugData?.Finish();
        context.Blackboard.ClearCurrentInvestigationRoute();
        context.Blackboard.ClearCurrentDestination();

        // She goes back to patrolling, but not back to where patrolling had
        // got to.
        //
        // Losing somebody used to cost her nothing at all: the search ended,
        // the route carried on from whichever point it had been interrupted
        // at, and within a few seconds she was somewhere else in the level
        // behaving exactly as she had before she ever saw anyone. A player who
        // was nearly caught could wait fifteen seconds and walk back into the
        // same room, because nothing about that room was different.
        //
        // Rejoining the loop at the point nearest where the search ran out
        // keeps her in that part of the level for a while, without a raised
        // alert state, a suspicion timer, or anything else that has to be
        // tuned and decay and be explained to the player. She is not angrier.
        // She is just still nearby, which is the thing that makes coming back
        // out feel like a decision.
        //
        // Only when the search had somewhere to be. An investigation that
        // ended because it could not work out where to go in the first place
        // knows nothing about the level, and its origin is the world origin.
        if (hasInvestigationOrigin)
        {
            context.PatrolController?.ResumeNearest(investigationOrigin);
        }

        context.ClearAllTargetMemory();
        context.ReturnToDefaultBehaviour();
    }

    private void ResetRuntimeState()
    {
        phase = InvestigationPhase.MovingToLastKnownPosition;

        investigationOrigin = default;
        hasInvestigationOrigin = false;
        currentDestination = default;

        currentSearchPointIndex = 0;
        repathTimer = 0f;
        dwellTimer = 0f;
        dwellDurationTotal = 0f;
        dwellArrivalForward = Vector3.forward;
        isDwelling = false;

        hasDestination = false;
        hidingPlaceCheck?.Reset();

        context.Blackboard.ClearCurrentDestination();
        context.Blackboard.ClearCurrentInvestigationRoute();
    }
}
