using UnityEngine;

public sealed class EnemyInvestigateState : IEnemyStateHandler
{
    private enum InvestigationPhase
    {
        MovingToLastKnownPosition,
        SearchingArea
    }

    private readonly EnemyBrainContext context;
    private readonly EnemyInvestigationSearchPlanner searchPlanner;

    private InvestigationPhase phase;

    private Vector3 investigationOrigin;
    private Vector3 currentDestination;

    private int currentSearchPointIndex;
    private float searchTimer;
    private float repathTimer;

    private bool hasDestination;

    public EnemyState State => EnemyState.Investigate;

    public EnemyInvestigateState(EnemyBrainContext context)
    {
        this.context = context;

        int maxSearchPoints = context != null && context.Config != null
            ? context.Config.investigationSearchPointCount
            : 0;

        searchPlanner = new EnemyInvestigationSearchPlanner(maxSearchPoints);
    }

    public void Enter()
    {
        ResetRuntimeState();

        if (!TryResolveInvestigationOrigin(out investigationOrigin))
        {
            FinishInvestigation();
            return;
        }

        phase = InvestigationPhase.MovingToLastKnownPosition;
        SetDestination(investigationOrigin, context.Config.chaseSpeed);
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

        TickSearchingArea(deltaTime);
    }

    public void Exit()
    {
        ResetRuntimeState();
    }

    private void TickMovingToLastKnownPosition()
    {
        if (!hasDestination)
        {
            SetDestination(investigationOrigin, context.Config.chaseSpeed);
            return;
        }

        RepathIfNeeded(context.Config.chaseSpeed);

        if (!context.Navigator.HasReached(context.Config.investigationReachDistance))
        {
            return;
        }

        StartAreaSearch();
    }

    private void StartAreaSearch()
    {
        phase = InvestigationPhase.SearchingArea;
        searchTimer = context.Config.investigationSearchDuration;
        currentSearchPointIndex = 0;

        context.TargetMemory.ClearPrimaryInvestigationPosition();

        searchPlanner.BuildSearchPoints(
            investigationOrigin,
            context.Navigator.Position,
            context.Config.investigationSearchRadius,
            context.Config.investigationSearchPointCount
        );

        if (searchTimer <= 0f || searchPlanner.PointCount == 0)
        {
            TryMoveToSecondaryOrFinish();
            return;
        }

        MoveToNextSearchPointOrFinish();
    }

    private void TickSearchingArea(float deltaTime)
    {
        searchTimer -= deltaTime;

        if (searchTimer <= 0f)
        {
            TryMoveToSecondaryOrFinish();
            return;
        }

        if (!hasDestination)
        {
            MoveToNextSearchPointOrFinish();
            return;
        }

        RepathIfNeeded(context.Config.investigationSearchSpeed);

        if (!context.Navigator.HasReached(context.Config.investigationReachDistance))
        {
            return;
        }

        MoveToNextSearchPointOrFinish();
    }

    private void MoveToNextSearchPointOrFinish()
    {
        if (!searchPlanner.TryGetPoint(currentSearchPointIndex, out Vector3 nextPoint))
        {
            TryMoveToSecondaryOrFinish();
            return;
        }

        currentSearchPointIndex++;
        SetDestination(nextPoint, context.Config.investigationSearchSpeed);
    }

    private void TryMoveToSecondaryOrFinish()
    {
        if (context.TargetMemory.PromoteSecondarySuspiciousPositionToLastKnown())
        {
            if (TryResolveInvestigationOrigin(out investigationOrigin))
            {
                phase = InvestigationPhase.MovingToLastKnownPosition;
                SetDestination(investigationOrigin, context.Config.chaseSpeed);
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

    private void SetDestination(Vector3 destination, float speed)
    {
        currentDestination = destination;
        hasDestination = context.Navigator.TryMoveTo(destination, speed);
        repathTimer = Mathf.Max(0.05f, context.Config.investigationRepathInterval);

        if (!hasDestination)
        {
            TryMoveToSecondaryOrFinish();
        }
    }

    private void RepathIfNeeded(float speed)
    {
        if (!hasDestination || repathTimer > 0f)
        {
            return;
        }

        hasDestination = context.Navigator.TryMoveTo(currentDestination, speed);
        repathTimer = Mathf.Max(0.05f, context.Config.investigationRepathInterval);

        if (!hasDestination)
        {
            TryMoveToSecondaryOrFinish();
        }
    }

    private void FinishInvestigation()
    {
        context.ClearAllTargetMemory();
        context.ReturnToDefaultBehaviour();
    }

    private void ResetRuntimeState()
    {
        phase = InvestigationPhase.MovingToLastKnownPosition;

        investigationOrigin = default;
        currentDestination = default;

        currentSearchPointIndex = 0;
        searchTimer = 0f;
        repathTimer = 0f;

        hasDestination = false;
    }
}