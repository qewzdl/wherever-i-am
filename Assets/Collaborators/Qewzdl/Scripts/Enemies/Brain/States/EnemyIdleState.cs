public sealed class EnemyIdleState : IEnemyStateHandler
{
    private readonly EnemyBrainContext context;

    public EnemyState State => EnemyState.Idle;

    public EnemyIdleState(EnemyBrainContext context)
    {
        this.context = context;
    }

    public void Enter()
    {
        context.Navigator.ResetPath();
    }

    public void Tick(float deltaTime)
    {
        if (context.TargetMemory.HasTarget)
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        if (context.HasPatrolRoute)
        {
            context.ChangeState(EnemyState.Patrol);
        }
    }

    public void Exit()
    {
    }
}