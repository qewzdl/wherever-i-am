using System.Collections.Generic;
using UnityEngine;

// Stands and watches. The first beat of going round behind someone rather
// than running at them: stop where you were seen from, keep facing them, and
// wait to find out whether they have noticed you.
public sealed class EnemyStalkState : IEnemyStateHandler
{

    private const float FacingDegreesPerSecond = 220f;

    private readonly EnemyBrainContext context;

    private float watchedTimer;
    private float lostTimer;

    public EnemyState State => EnemyState.Stalk;

    public EnemyStalkState(EnemyBrainContext context)
    {
        this.context = context;
    }

    public void Enter()
    {
        watchedTimer = 0f;
        lostTimer = 0f;

        // Stopping is the whole point of the state, and it has to happen on
        // entry rather than being left to the first tick - a frame of walking
        // on reads as the enemy changing its mind.
        context.StopNavigation();
    }

    public void Tick(float deltaTime)
    {
        if (!context.TryContinueManeuver(
                out EnemyTargetObservation targetObservation))
        {
            return;
        }

        Vector3 selfPosition = context.Navigator.Position;
        EnemyTarget currentTarget = context.TargetMemory.CurrentTarget;

        // The manoeuvre's own target, not whoever perception is holding. They
        // are the same person unless something has gone wrong, and this is
        // where it would show.
        bool hasLiveTarget =
            currentTarget != null &&
            context.TargetMemory.IsCurrentTargetValid &&
            currentTarget == targetObservation.Target;

        // A lost target is not immediately a search. Vision refreshes four
        // times a second, so a target that steps behind a door frame is
        // "gone" for a moment on its way past - abandoning the stalk on that
        // meant the enemy dropped into a search over and over and never got
        // anywhere.
        if (!hasLiveTarget)
        {
            lostTimer += deltaTime;

            if (lostTimer >= context.Config.visualTargetMemoryDuration)
            {
                context.ForgetCurrentTargetButKeepLastKnownPosition();
                context.ChangeState(EnemyState.Investigate);
            }

            return;
        }

        lostTimer = 0f;

        Vector3 targetPosition =
            context.GetTargetNavigationPosition(currentTarget);
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

        IReadOnlyList<PlayerWatcher> watchers = context.GetWatchers();

        if (watchers.Count == 0)
        {
            watchedTimer = 0f;
            return;
        }

        // Being looked at for a moment is being noticed. A single frame is
        // not: a player sweeping the room past the enemy has not seen it.
        watchedTimer += deltaTime;

        if (watchedTimer >= context.Config.stalkNoticedDuration)
        {
            // The decision the whole manoeuvre turns on, made once and here:
            // the target does not change because somebody else noticed. A
            // second player spotting the enemy is a reason to break off, not a
            // reason to start creeping up on them instead - swapping would
            // throw away the approach already walked and hand the new person a
            // plan built for the old one. The retreat that follows backs away
            // from everyone watching, including whoever this was.
            context.ChangeState(EnemyState.Retreat);
        }
    }

    public void Exit()
    {
        watchedTimer = 0f;
        lostTimer = 0f;
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
