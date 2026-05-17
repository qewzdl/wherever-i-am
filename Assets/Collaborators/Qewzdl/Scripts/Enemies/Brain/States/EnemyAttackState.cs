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
            context.AttackController?.Interrupt(
                EnemyAttackResultType.InvalidTarget,
                context.Navigator.Position
            );

            MoveToInvestigationOrReturn();
            return;
        }

        if (!context.TargetMemory.IsCurrentTargetValid)
        {
            context.AttackController?.Interrupt(
                EnemyAttackResultType.InvalidTarget,
                context.Navigator.Position
            );

            context.ForgetCurrentTargetButKeepLastKnownPosition();
            MoveToInvestigationOrReturn();
            return;
        }

        Vector3 targetPosition = context.PerceptionMemory.IsUsingVisualMemory
            ? context.PerceptionMemory.GetVisualMemoryTargetPosition()
            : context.GetTargetNavigationPosition(context.TargetMemory.CurrentTarget);

        context.InvestigationMemory.RememberLastKnownTargetPosition(targetPosition);

        if (context.PerceptionMemory.IsUsingVisualMemory)
        {
            context.AttackController?.Interrupt(
                EnemyAttackResultType.Interrupted,
                context.Navigator.Position
            );

            context.ChangeState(EnemyState.Chase);
            return;
        }

        context.StopNavigation();

        if (context.AttackController != null && context.AttackController.IsBusy)
        {
            return;
        }

        float distanceToTarget = Vector3.Distance(context.Navigator.Position, targetPosition);

        if (distanceToTarget > context.Config.attackDistance)
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        EnemyAttackResult result = context.AttackController.TryStartAttack(
            context.TargetMemory.CurrentTarget,
            context.Config,
            context.Navigator.Position,
            context.AttackController
        );

        if (result.Type == EnemyAttackResultType.InvalidTarget)
        {
            context.ForgetCurrentTargetButKeepLastKnownPosition();
            MoveToInvestigationOrReturn();
            return;
        }

        if (result.Type == EnemyAttackResultType.OutOfRange)
        {
            context.ChangeState(EnemyState.Chase);
        }
    }

    public void Exit()
    {
        context.AttackController?.Interrupt(
            EnemyAttackResultType.Interrupted,
            context.Navigator.Position
        );
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