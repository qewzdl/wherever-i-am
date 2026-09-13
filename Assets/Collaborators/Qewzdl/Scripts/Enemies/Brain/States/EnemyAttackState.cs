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

        if (context.Navigator.IsDirectApproachBlockedByItem(targetPosition))
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        if (context.AttackController != null && context.AttackController.IsBusy)
        {
            // Winding up is not standing still.
            //
            // She used to stop dead the moment she decided to swing and hold
            // there for the whole windup, which is a third of a second of her
            // promising not to move while the player is under no such promise.
            // Walking backwards covers about a metre in that time - past the
            // reach of the blow - so the swing was cancelled, she closed
            // again, stopped again, and a player who simply kept stepping back
            // could do it for ever.
            //
            // So the windup follows. Backing away now delays the blow instead
            // of undoing it, and getting away from her means getting away from
            // her: breaking her sight, or putting something between you.
            //
            // Only the windup. The commit is the blow landing and the recovery
            // is her open - both of those are meant to be moments she is not
            // going anywhere, and they are what a player reads to time an
            // escape.
            if (context.AttackController.Phase == EnemyAttackPhase.AttackWindup)
                context.TryMoveTo(targetPosition, context.Config.chaseSpeed, allowPushThrough: true);
            else
                context.StopNavigation();

            return;
        }

        float distanceToTarget = Vector3.Distance(context.Navigator.Position, targetPosition);

        if (distanceToTarget > context.Config.attackDistance)
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        if (!context.AttackController.TryValidateAttackTarget(
                context.TargetMemory.CurrentTarget,
                context.Config,
                context.Navigator.Position,
                context.AttackController,
                out EnemyAttackResultType validationFailureType
            ))
        {
            if (validationFailureType == EnemyAttackResultType.InvalidTarget)
            {
                context.ForgetCurrentTargetButKeepLastKnownPosition();
                MoveToInvestigationOrReturn();
                return;
            }

            context.ChangeState(EnemyState.Chase);
            return;
        }

        context.StopNavigation();

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
