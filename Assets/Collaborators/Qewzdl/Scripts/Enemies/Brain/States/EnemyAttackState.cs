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
        context.Navigator.ResetPath();
        context.Navigator.Stop();
    }

    public void Tick(float deltaTime)
    {
        if (!context.TargetMemory.HasTarget)
        {
            if (context.TargetMemory.HasLastKnownTargetPosition)
            {
                context.ChangeState(EnemyState.Investigate);
            }
            else
            {
                context.ReturnToDefaultBehaviour();
            }

            return;
        }

        if (!context.TargetMemory.IsCurrentTargetValid)
        {
            context.ClearAllTargetMemory();
            context.ReturnToDefaultBehaviour();
            return;
        }

        Vector3 targetPosition = context.GetTargetNavigationPosition(context.TargetMemory.CurrentTarget);
        float distanceToTarget = Vector3.Distance(context.Navigator.Position, targetPosition);

        if (distanceToTarget > context.Config.attackDistance)
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        context.Navigator.Stop();

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
}