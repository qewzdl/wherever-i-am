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

        if (distanceToTarget > context.Config.loseTargetDistance)
        {
            context.ClearTargetOnly();

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

        if (distanceToTarget <= context.Config.attackDistance)
        {
            context.ChangeState(EnemyState.Attack);
            return;
        }

        context.Navigator.TryMoveTo(targetPosition, context.Config.chaseSpeed);
    }

    public void Exit()
    {
    }
}