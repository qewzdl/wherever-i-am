using UnityEngine;

// Stands behind the target and waits for it to turn round.
//
// The moment it is looked at, it goes. Handing over to chasing rather than
// attacking here on purpose: chasing already knows how to close the last
// couple of metres and hand over to the attack, and a second copy of that
// would be a second thing to keep correct.
public sealed class EnemyAmbushState : IEnemyStateHandler
{
    private const float FacingDegreesPerSecond = 260f;

    private readonly EnemyBrainContext context;

    private float waitTimer;

    public EnemyState State => EnemyState.Ambush;

    public EnemyAmbushState(EnemyBrainContext context)
    {
        this.context = context;
    }

    public void Enter()
    {
        waitTimer = 0f;
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

        FacePlayer(targetObservation.Position, selfPosition, deltaTime);

        // Turned round and seen it. This is the beat the whole sequence was
        // built for. The stealth approach has completed, so the chase is an
        // assault rather than an ordinary distance-based chase; otherwise a
        // target that opened the gap on this frame sent the enemy straight
        // back to Stalk on the next perception refresh.
        if (context.IsSelfSeenByAnyone())
        {
            context.GiveUpOnStealth(
                EnemyStealthFailureReason.AmbushSprung
            );
            return;
        }

        waitTimer += deltaTime;

        // Wandered off out of reach. Nothing to spring on any more.
        //
        // Not back to watching: standing still and watching them walk away is
        // how the sequence used to restart itself from the top with a longer
        // walk in front of it. Either there is an attempt left in this pursuit,
        // in which case coming round to where they are now is that attempt, or
        // there is not - and then the attempt is over because they moved,
        // which is a different thing from the enemy having failed to hide.
        if (Vector3.Distance(selfPosition, targetObservation.Position) >
            context.Config.stalkInsteadOfChasingDistance)
        {
            if (context.EngagementTactics.CanStartAnotherStealthAttempt(
                    context.Config.StealthTactics
                        .maxStealthAttemptsPerEngagement))
            {
                context.ChangeState(EnemyState.Flank);
                return;
            }

            context.GiveUpOnStealth(EnemyStealthFailureReason.TargetMoved);
            return;
        }

        // Never turned round. Going back to stalking here left the whole
        // sequence with no ending - watch, circle, wait, give up, watch again,
        // for as long as the player never looked behind them. Patience running
        // out is the enemy deciding it has waited long enough, and the
        // commitment is what stops the next distance check undeciding it.
        if (waitTimer >= context.Config.ambushPatience)
        {
            context.GiveUpOnStealth(EnemyStealthFailureReason.Timeout);
        }
    }

    public void Exit()
    {
        waitTimer = 0f;
    }

    private void FacePlayer(
        Vector3 playerPosition,
        Vector3 selfPosition,
        float deltaTime
    )
    {
        Vector3 toPlayer = playerPosition - selfPosition;
        toPlayer.y = 0f;

        context.Navigator.FaceDirection(
            toPlayer,
            FacingDegreesPerSecond,
            deltaTime
        );
    }
}
