using UnityEngine;

public sealed class EnemyInvestigateState : IEnemyStateHandler
{
    private enum InvestigationPhase
    {
        MovingToLastKnownPosition,
        FollowingSearchRoute
    }

    private readonly EnemyBrainContext context;
    private readonly EnemyInvestigationSearchPlanner searchPlanner = new();

    private InvestigationPhase phase;

    private Vector3 investigationOrigin;
    private Vector3 currentDestination;

    private int currentSearchPointIndex;
    private float repathTimer;

    private bool hasDestination;

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

        StartHierarchicalSearch();
    }

    private void StartHierarchicalSearch()
    {
        phase = InvestigationPhase.FollowingSearchRoute;
        currentSearchPointIndex = 0;

        context.TargetMemory.ClearPrimaryInvestigationPosition();

        searchPlanner.BuildHierarchicalSearchPlan(
            investigationOrigin,
            context.Navigator.Position,
            context.Config.investigationBranchRadius,
            context.Config.investigationBranchPointCount,
            context.Config.investigationLeafRadius,
            context.Config.investigationLeafPointCountPerBranch
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
            return;
        }

        TryMoveToSecondaryOrFinish();
    }

    private void TryMoveToSecondaryOrFinish()
    {
        if (context.TargetMemory.PromoteSecondarySuspiciousPositionToLastKnown())
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
                    context.Config.investigationLeafPointCountPerBranch
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
        if (context.TargetMemory.TryGetLastKnownTargetPosition(out position))
        {
            return true;
        }

        if (context.TargetMemory.PromoteSecondarySuspiciousPositionToLastKnown())
        {
            return context.TargetMemory.TryGetLastKnownTargetPosition(out position);
        }

        position = default;
        return false;
    }

    private bool TrySetDestination(Vector3 destination, float speed)
    {
        currentDestination = destination;
        hasDestination = context.TryMoveTo(destination, speed);
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

    private void RepathToCurrentDestination(float speed)
    {
        if (!hasDestination || repathTimer > 0f)
        {
            return;
        }

        hasDestination = context.TryMoveTo(currentDestination, speed);
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

        hasDestination = false;

        context.Blackboard.ClearCurrentDestination();
        context.Blackboard.ClearCurrentInvestigationRoute();
    }
}