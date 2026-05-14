using UnityEngine;

public sealed class EnemyChaseState : IEnemyStateHandler
{
    private readonly EnemyBrainContext context;

    public EnemyState State => EnemyState.Chase;

    public EnemyChaseState(EnemyBrainContext context)
    {
        this.context = context;
    }

    public void Enter()
    {
    }

    public void Tick(float deltaTime)
    {
        if (!context.TargetMemory.HasTarget)
        {
            MoveToInvestigationOrReturn();
            return;
        }

        if (!context.TargetMemory.IsCurrentTargetValid)
        {
            context.ForgetCurrentTargetButKeepLastKnownPosition();
            MoveToInvestigationOrReturn();
            return;
        }

        Vector3 targetPosition = context.TargetMemory.IsUsingVisualMemory
            ? context.TargetMemory.GetCurrentTargetPosition()
            : context.GetTargetNavigationPosition(context.TargetMemory.CurrentTarget);

        context.TargetMemory.RememberPosition(targetPosition);

        float distanceToTarget = Vector3.Distance(context.Navigator.Position, targetPosition);

        if (distanceToTarget > context.Config.loseTargetDistance)
        {
            context.ForgetCurrentTargetButKeepLastKnownPosition();
            MoveToInvestigationOrReturn();
            return;
        }

        if (!context.TargetMemory.IsUsingVisualMemory &&
            distanceToTarget <= context.Config.attackDistance)
        {
            context.ChangeState(EnemyState.Attack);
            return;
        }

        context.Navigator.TryMoveTo(targetPosition, context.Config.chaseSpeed);
    }

    public void Exit()
    {
    }

    private void MoveToInvestigationOrReturn()
    {
        if (context.TargetMemory.HasLastKnownTargetPosition)
        {
            context.ChangeState(EnemyState.Investigate);
            return;
        }

        context.ClearAllTargetMemory();
        context.ReturnToDefaultBehaviour();
    }
}