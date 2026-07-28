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

        repathTimer -= deltaTime;

        if (phase == InvestigationPhase.MovingToLastKnownPosition)
        {
            TickMovingToLastKnownPosition();
            return;
        }

        if (phase == InvestigationPhase.CheckingHidingPlace)
        {
            TickCheckingHidingPlace(deltaTime);
            return;
        }

        TickFollowingSearchRoute();
    }

    public void Exit()
    {
        context.InvestigationDebugData?.Finish();
        ResetRuntimeState();
    }

    private void TickMovingToLastKnownPosition()
    {
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

        if (TryStartHidingPlaceCheck())
        {
            return;
        }

        StartHierarchicalSearch();
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

        checkedHidingPlace = hidingPlace;
        HidingPlaceData settings = hidingPlace.Configuration;
        hidingCheckTimer =
            (settings != null
                ? settings.EnterDuration + settings.ExitDuration
                : 0f) + 0.5f;
        phase = InvestigationPhase.CheckingHidingPlace;

        context.StopNavigation();

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
        context.Blackboard.SetCurrentInvestigationRoute(searchPlanner.Points);

        if (searchPlanner.PointCount == 0)
        {
            TryMoveToSecondaryOrFinish();
            return;
        }

        MoveToNextSearchPointOrFinish();
    }

    private void TickFollowingSearchRoute()
    {
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

    private bool TrySetDestination(Vector3 destination, float speed)
    {
        currentDestination = destination;
        // A search waypoint that turns out unreachable should just be
        // skipped in favour of the next one, not force the enemy to push
        // furniture aside to reach an arbitrary point in its search plan.
        hasDestination = context.TryMoveTo(
            destination,
            speed,
            allowBarrierPushThrough: false);
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
            allowBarrierPushThrough: false);
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

        hasDestination = false;
        checkedHidingPlace = null;

        context.Blackboard.ClearCurrentDestination();
        context.Blackboard.ClearCurrentInvestigationRoute();
    }
}
