public sealed class EnemyPatrolState : IEnemyStateHandler
{
    private readonly EnemyBrainContext context;

    public EnemyState State => EnemyState.Patrol;

    public EnemyPatrolState(EnemyBrainContext context)
    {
        this.context = context;
    }

    public void Enter()
    {
        if (!context.HasPatrolRoute)
        {
            context.ChangeState(EnemyState.Idle);
            return;
        }

        context.PatrolController.MoveToNextPoint();
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

        if (context.PatrolController.HasReachedCurrentPoint())
        {
            context.PatrolController.MoveToNextPoint();
        }
    }

    public void Exit()
    {
    }
}