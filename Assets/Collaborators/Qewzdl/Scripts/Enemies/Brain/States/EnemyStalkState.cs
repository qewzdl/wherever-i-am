using UnityEngine;

// Stands and watches. The first beat of going round behind someone rather
// than running at them: stop where you were seen from, keep facing them, and
// wait to find out whether they have noticed you.
public sealed class EnemyStalkState : IEnemyStateHandler
{
    // Roughly how tall the enemy is, for asking whether a player can see it.
    // A single point at the feet is hidden by every crate in the level.
    private const float BodyHeight = 1.8f;

    private const float FacingDegreesPerSecond = 220f;

    private readonly EnemyBrainContext context;

    private float watchedTimer;

    public EnemyState State => EnemyState.Stalk;

    public EnemyStalkState(EnemyBrainContext context)
    {
        this.context = context;
    }

    public void Enter()
    {
        watchedTimer = 0f;

        // Stopping is the whole point of the state, and it has to happen on
        // entry rather than being left to the first tick - a frame of walking
        // on reads as the enemy changing its mind.
        context.StopNavigation();
    }

    public void Tick(float deltaTime)
    {
        if (!context.TargetMemory.HasTarget ||
            !context.TargetMemory.IsCurrentTargetValid)
        {
            context.ForgetCurrentTargetButKeepLastKnownPosition();
            context.ChangeState(EnemyState.Investigate);
            return;
        }

        Vector3 targetPosition =
            context.GetTargetNavigationPosition(context.TargetMemory.CurrentTarget);
        Vector3 selfPosition = context.Navigator.Position;
        float distance = Vector3.Distance(selfPosition, targetPosition);

        if (distance > context.Config.loseTargetDistance)
        {
            context.ForgetCurrentTargetButKeepLastKnownPosition();
            context.ChangeState(EnemyState.Investigate);
            return;
        }

        // Closed the gap on its own - the target walked in. There is nothing
        // left to circle round.
        if (distance <= context.Config.chaseWithoutStalkingDistance)
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        FaceTarget(targetPosition, selfPosition, deltaTime);

        if (!PlayerGazeNetwork.IsBodySeenByAnyone(selfPosition, BodyHeight))
        {
            watchedTimer = 0f;
            return;
        }

        // Being looked at for a moment is being noticed. A single frame is
        // not: a player sweeping the room past the enemy has not seen it.
        watchedTimer += deltaTime;

        if (watchedTimer >= context.Config.stalkNoticedDuration)
        {
            context.ChangeState(EnemyState.Retreat);
        }
    }

    public void Exit()
    {
        watchedTimer = 0f;
    }

    private void FaceTarget(
        Vector3 targetPosition,
        Vector3 selfPosition,
        float deltaTime
    )
    {
        Vector3 toTarget = targetPosition - selfPosition;
        toTarget.y = 0f;

        context.Navigator.FaceDirection(
            toTarget,
            FacingDegreesPerSecond,
            deltaTime
        );
    }
}
