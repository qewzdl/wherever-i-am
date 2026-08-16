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
    private Vector3 currentDestination;

    private int currentSearchPointIndex;
    private float repathTimer;
    private float hidingCheckTimer;
    private float dwellTimer;
    private float dwellDurationTotal;
    private Vector3 dwellArrivalForward;
    private bool isDwelling;

    private bool hasDestination;
    private HidingPlaceInteractable checkedHidingPlace;

    public EnemyState State => EnemyState.Investigate;

    public EnemyInvestigateState(EnemyBrainContext context)
    {
        this.context = context;
    }

    public void Enter()
    {
        ResetRuntimeState();

        if (!TryResolveInvestigationOrigin(out investigationOrigin))
        {
            FinishInvestigation();
            return;
        }

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

            if (TryStartHidingPlaceCheck())
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

        if (TryBeginPointDwell())
        {
            return;
        }

        if (TryStartHidingPlaceCheck())
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
        if (context.InvestigationMemory.HasObservedHidingPlace)
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

    private bool TryStartHidingPlaceCheck()
    {
        HidingPlaceInteractable hidingPlace =
            context.InvestigationMemory.ObservedHidingPlace;

        if (hidingPlace == null ||
            !hidingPlace.IsSpawned)
        {
            context.InvestigationMemory.ClearObservedHidingPlace();
            return false;
        }

        if (hidingPlace.State != HidingTransitionState.Entering &&
            hidingPlace.State != HidingTransitionState.Occupied)
        {
            context.InvestigationMemory.ClearObservedHidingPlace();
            return false;
        }

        // No relevance check on the reference itself: it is only ever written
        // for a target this enemy watched vanish into this box, and the
        // investigation origin is that box. Enemies that saw nothing arrive
        // here with no reference at all and bail out above.
        HidingPlaceData settings = hidingPlace.Configuration;

        checkedHidingPlace = hidingPlace;
        hidingCheckTimer =
            (settings != null
                ? settings.EnterDuration + settings.ExitDuration
                : 0f) + 0.5f;
        phase = InvestigationPhase.CheckingHidingPlace;

        // Walk up to the hiding place instead of stopping where the stimulus
        // was: the server only opens it from within EnemyInvestigationDistance,
        // and the investigation origin can sit a metre or two off the anchor.
        if (!TrySetDestination(
                hidingPlace.EnemyInvestigationPosition,
                context.Config.chaseSpeed))
        {
            context.StopNavigation();
        }

        if (hidingPlace.State == HidingTransitionState.Occupied)
        {
            TryOpenCheckedHidingPlace();
        }

        return true;
    }

    private void TickCheckingHidingPlace(float deltaTime)
    {
        hidingCheckTimer -= Mathf.Max(0f, deltaTime);

        if (checkedHidingPlace == null ||
            !checkedHidingPlace.IsSpawned ||
            hidingCheckTimer <= 0f)
        {
            context.InvestigationMemory.ClearObservedHidingPlace();
            checkedHidingPlace = null;
            StartHierarchicalSearch();
            return;
        }

        RepathToCurrentDestination(context.Config.chaseSpeed);

        if (checkedHidingPlace.State ==
            HidingTransitionState.Entering)
        {
            return;
        }

        if (checkedHidingPlace.State ==
            HidingTransitionState.Occupied &&
            TryOpenCheckedHidingPlace())
        {
            return;
        }

        if (checkedHidingPlace.State ==
            HidingTransitionState.Exiting)
        {
            return;
        }

        context.InvestigationMemory.ClearObservedHidingPlace();
        checkedHidingPlace = null;
        StartHierarchicalSearch();
    }

    private bool TryOpenCheckedHidingPlace()
    {
        if (checkedHidingPlace == null ||
            !checkedHidingPlace.TryInvestigateServer(
                context.Navigator.Position))
        {
            return false;
        }

        context.InvestigationMemory.ClearObservedHidingPlace();
        return true;
    }

    private void StartHierarchicalSearch()
    {
        phase = InvestigationPhase.FollowingSearchRoute;
        currentSearchPointIndex = 0;

        context.InvestigationMemory.ClearLastKnownTargetPosition();

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

        if (searchPlanner.PointCount == 0)
        {
            TryMoveToSecondaryOrFinish();
            return;
        }

        MoveToNextSearchPointOrFinish();
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
        HidingPlaceInteractable hidingPlace =
            context.InvestigationMemory.ObservedHidingPlace;

        if (hidingPlace != null && hidingPlace.IsSpawned)
        {
            position = hidingPlace.EnemyInvestigationPosition;
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

        context.ClearAllTargetMemory();
        context.ReturnToDefaultBehaviour();
    }

    private void ResetRuntimeState()
    {
        phase = InvestigationPhase.MovingToLastKnownPosition;

        investigationOrigin = default;
        currentDestination = default;

        currentSearchPointIndex = 0;
        repathTimer = 0f;
        hidingCheckTimer = 0f;
        dwellTimer = 0f;
        dwellDurationTotal = 0f;
        dwellArrivalForward = Vector3.forward;
        isDwelling = false;

        hasDestination = false;
        checkedHidingPlace = null;

        context.Blackboard.ClearCurrentDestination();
        context.Blackboard.ClearCurrentInvestigationRoute();
    }
}
