public sealed class EnemyInvestigateState : IEnemyStateHandler
{
    private readonly EnemyBrainContext context;

    public EnemyState State => EnemyState.Investigate;

    public EnemyInvestigateState(EnemyBrainContext context)
    {
        this.context = context;
    }

    public void Enter()
    {
        if (!context.TargetMemory.HasLastKnownTargetPosition)
        {
            context.ReturnToDefaultBehaviour();
            return;
        }

        if (!context.Navigator.TryMoveTo(
            context.TargetMemory.LastKnownTargetPosition,
            context.Config.chaseSpeed
        ))
        {
            context.ClearAllTargetMemory();
            context.ReturnToDefaultBehaviour();
        }
    }

    public void Tick(float deltaTime)
    {
        if (context.TargetMemory.HasTarget)
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        if (!context.TargetMemory.HasLastKnownTargetPosition)
        {
            context.ReturnToDefaultBehaviour();
            return;
        }

        context.Navigator.TryMoveTo(
            context.TargetMemory.LastKnownTargetPosition,
            context.Config.chaseSpeed
        );

        if (context.Navigator.HasReached(context.Config.patrolPointReachDistance))
        {
            context.ClearAllTargetMemory();
            context.ReturnToDefaultBehaviour();
        }
    }

    public void Exit()
    {
    }
}