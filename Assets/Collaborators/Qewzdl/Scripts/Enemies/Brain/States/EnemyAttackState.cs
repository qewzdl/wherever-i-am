using UnityEngine;

public sealed class EnemyAttackState : IEnemyStateHandler
{
    private readonly EnemyBrainContext context;

    public EnemyState State => EnemyState.Attack;

    public EnemyAttackState(EnemyBrainContext context)
    {
        this.context = context;
    }

    public void Enter()
    {
        context.ResetNavigationPath();
        context.StopNavigation();
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

        if (context.TargetMemory.IsUsingVisualMemory)
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        if (distanceToTarget > context.Config.attackDistance)
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        context.StopNavigation();

        context.AttackController.TryAttack(
            context.TargetMemory.CurrentTarget,
            context.Config,
            context.Navigator.Position,
            context.AttackController
        );
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