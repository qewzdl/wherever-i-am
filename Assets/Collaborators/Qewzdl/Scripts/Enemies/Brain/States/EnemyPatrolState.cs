public sealed class EnemyPatrolState : IEnemyStateHandler
{
    private enum PatrolPhase
    {
        MovingToRoutePoint,
        WanderingAtRoutePoint
    }

    private readonly EnemyBrainContext context;

    private PatrolPhase phase;
    private float stopTimer;
    private float wanderRetryTimer;

    public EnemyState State => EnemyState.Patrol;

    public EnemyPatrolState(EnemyBrainContext context)
    {
        this.context = context;
    }

    public void Enter()
    {
        ResetRuntimeState();

        if (!context.HasPatrolRoute)
        {
            context.ChangeState(EnemyState.Idle);
            return;
        }

        MoveToNextRoutePointOrIdle();
    }

    public void Tick(float deltaTime)
    {
        if (context.TargetMemory.HasTarget)
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        if (!context.HasPatrolRoute)
        {
            context.ChangeState(EnemyState.Idle);
            return;
        }

        if (phase == PatrolPhase.MovingToRoutePoint)
        {
            TickMovingToRoutePoint();
            return;
        }

        TickWanderingAtRoutePoint(deltaTime);
    }

    public void Exit()
    {
        ResetRuntimeState();
    }

    private void TickMovingToRoutePoint()
    {
        if (!context.PatrolController.HasReachedCurrentRoutePoint())
        {
            return;
        }

        if (!context.PatrolController.ShouldUseStopWander())
        {
            MoveToNextRoutePointOrIdle();
            return;
        }

        StartWanderingAtRoutePoint();
    }

    private void StartWanderingAtRoutePoint()
    {
        phase = PatrolPhase.WanderingAtRoutePoint;
        stopTimer = context.Config.patrolStopDuration;
        wanderRetryTimer = 0f;

        context.PatrolController.ClearActiveWanderDestination();
        TryMoveToNextWanderPoint();
    }

    private void TickWanderingAtRoutePoint(float deltaTime)
    {
        stopTimer -= deltaTime;

        if (stopTimer <= 0f)
        {
            MoveToNextRoutePointOrIdle();
            return;
        }

        if (context.PatrolController.HasReachedActiveWanderDestination())
        {
            context.PatrolController.ClearActiveWanderDestination();
        }

        if (context.PatrolController.HasActiveWanderDestination)
        {
            return;
        }

        wanderRetryTimer -= deltaTime;

        if (wanderRetryTimer > 0f)
        {
            return;
        }

        TryMoveToNextWanderPoint();
    }

    private void TryMoveToNextWanderPoint()
    {
        if (context.PatrolController.MoveToRandomPointAroundCurrentRoutePoint())
        {
            wanderRetryTimer = 0f;
            return;
        }

        wanderRetryTimer = 0.5f;
    }

    private void MoveToNextRoutePointOrIdle()
    {
        phase = PatrolPhase.MovingToRoutePoint;
        stopTimer = 0f;
        wanderRetryTimer = 0f;

        if (!context.PatrolController.MoveToNextRoutePoint())
        {
            context.ChangeState(EnemyState.Idle);
        }
    }

    private void ResetRuntimeState()
    {
        phase = PatrolPhase.MovingToRoutePoint;
        stopTimer = 0f;
        wanderRetryTimer = 0f;
    }
}