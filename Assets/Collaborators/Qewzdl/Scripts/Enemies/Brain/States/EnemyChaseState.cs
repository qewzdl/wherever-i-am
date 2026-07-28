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

        Vector3 targetPosition = context.PerceptionMemory.IsUsingVisualMemory
            ? context.PerceptionMemory.GetVisualMemoryTargetPosition()
            : context.GetTargetNavigationPosition(context.TargetMemory.CurrentTarget);

        context.InvestigationMemory.RememberLastKnownTargetPosition(targetPosition);

        float distanceToTarget = Vector3.Distance(context.Navigator.Position, targetPosition);

        if (distanceToTarget > context.Config.loseTargetDistance)
        {
            context.ForgetCurrentTargetButKeepLastKnownPosition();
            MoveToInvestigationOrReturn();
            return;
        }

        if (!context.PerceptionMemory.IsUsingVisualMemory &&
            distanceToTarget <= context.Config.attackDistance)
        {
            if (context.Navigator.IsDirectApproachBlockedByItem(targetPosition))
            {
                context.TryMoveTo(targetPosition, context.Config.chaseSpeed, allowPushThrough: true);
                return;
            }

            if (context.AttackController.TryValidateAttackTarget(
                    context.TargetMemory.CurrentTarget,
                    context.Config,
                    context.Navigator.Position,
                    context.AttackController,
                    out EnemyAttackResultType failureType
                ))
            {
                context.ChangeState(EnemyState.Attack);
                return;
            }

            if (failureType == EnemyAttackResultType.InvalidTarget)
            {
                context.ForgetCurrentTargetButKeepLastKnownPosition();
                MoveToInvestigationOrReturn();
                return;
            }

            context.TryMoveTo(targetPosition, context.Config.chaseSpeed, allowPushThrough: true);
            return;
        }

        context.TryMoveTo(targetPosition, context.Config.chaseSpeed, allowPushThrough: true);
    }

    public void Exit()
    {
    }

    private void MoveToInvestigationOrReturn()
    {
        if (context.InvestigationMemory.HasLastKnownTargetPosition)
        {
            context.ChangeState(EnemyState.Investigate);
            return;
        }

        context.ClearAllTargetMemory();
        context.ReturnToDefaultBehaviour();
    }
}
