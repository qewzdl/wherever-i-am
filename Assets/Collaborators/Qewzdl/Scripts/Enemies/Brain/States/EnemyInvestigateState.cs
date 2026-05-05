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
        if (!EnsureInvestigationPosition())
        {
            context.ReturnToDefaultBehaviour();
            return;
        }

        MoveToInvestigationPosition();
    }

    public void Tick(float deltaTime)
    {
        if (context.TargetMemory.HasTarget)
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        if (!EnsureInvestigationPosition())
        {
            context.ReturnToDefaultBehaviour();
            return;
        }

        MoveToInvestigationPosition();

        if (!context.Navigator.HasReached(context.Config.patrolPointReachDistance))
        {
            return;
        }

        context.TargetMemory.ClearPrimaryInvestigationPosition();

        if (context.TargetMemory.PromoteSecondarySuspiciousPositionToLastKnown())
        {
            MoveToInvestigationPosition();
            return;
        }

        context.ClearAllTargetMemory();
        context.ReturnToDefaultBehaviour();
    }

    public void Exit()
    {
    }

    private bool EnsureInvestigationPosition()
    {
        if (context.TargetMemory.HasLastKnownTargetPosition)
        {
            return true;
        }

        return context.TargetMemory.PromoteSecondarySuspiciousPositionToLastKnown();
    }

    private void MoveToInvestigationPosition()
    {
        if (!context.Navigator.TryMoveTo(
            context.TargetMemory.LastKnownTargetPosition,
            context.Config.chaseSpeed
        ))
        {
            context.ClearAllTargetMemory();
            context.ReturnToDefaultBehaviour();
        }
    }
}