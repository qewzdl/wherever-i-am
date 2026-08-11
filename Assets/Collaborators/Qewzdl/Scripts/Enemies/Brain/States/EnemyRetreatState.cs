using System.Collections.Generic;
using UnityEngine;

// Breaks off once it has been noticed. Backs away from everyone watching until
// it is out of sight, then hands back to flanking to come round from
// somewhere else.
//
// Backs away from everyone, not from its target. The state used to plan
// against the one pose it had observed, so an enemy spotted by a second player
// retreated relative to the first - which in a shared room is a route towards
// the person who had just noticed it.
public sealed class EnemyRetreatState : IEnemyStateHandler
{
    private readonly EnemyBrainContext context;
    private readonly EnemyRetreatPlanner planner;
    private readonly List<Vector3> threats = new();

    private float repathTimer;
    private float unseenTimer;
    private float giveUpTimer;
    private Vector3 retreatPoint;
    private bool hasRetreatPoint;

    public EnemyState State => EnemyState.Retreat;

    public EnemyRetreatState(EnemyBrainContext context)
    {
        this.context = context;
        planner = new EnemyRetreatPlanner(context);
    }

    public void Enter()
    {
        repathTimer = 0f;
        unseenTimer = 0f;
        giveUpTimer = 0f;
        hasRetreatPoint = false;
        planner.Restart();
    }

    public void Tick(float deltaTime)
    {
        // Breaking sight is this state's job, so it continues from the
        // manoeuvre's own observation. Looking up the nearest live player here
        // transfers the retreat to an unrelated client when the original
        // target hides or disconnects.
        if (!context.TryContinueManeuver(
                out EnemyTargetObservation targetObservation))
        {
            return;
        }

        Vector3 selfPosition = context.Navigator.Position;

        giveUpTimer += deltaTime;

        // Backing off only works if there is somewhere out of sight to back
        // off to. In a lit open room with several players there is not, and
        // without this the enemy shuffled between fallback points for as long
        // as anyone kept looking at it - the state had no ending that did not
        // depend on the players' behaviour. Giving up and coming at them is
        // the same answer the flank reaches for the same reason.
        if (giveUpTimer >= context.Config.retreatTimeout)
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        IReadOnlyList<PlayerWatcher> watchers = context.GetWatchers();
        CollectThreats(watchers, targetObservation.Position);

        if (watchers.Count > 0)
        {
            unseenTimer = 0f;
        }
        else
        {
            unseenTimer += deltaTime;

            // Out of sight long enough to move. Going round rather than
            // back to watching: returning to stalking made a closed loop -
            // watch, be seen, back off, watch again - which is what the first
            // build did, seven times over, and it reads as aimless wandering.
            if (unseenTimer >= context.Config.retreatBrokenSightDuration)
            {
                context.ChangeState(EnemyState.Flank);
                return;
            }
        }

        repathTimer -= deltaTime;

        if (repathTimer > 0f)
        {
            return;
        }

        EnemyStealthTacticsConfig tactics = context.Config.StealthTactics;
        repathTimer = tactics.retreatRepathInterval;

        // A path query per candidate now, because a candidate is only good if
        // the route to it is.
        int candidateCount = Mathf.Clamp(
            tactics.candidatesPerTick,
            1,
            tactics.retreatAngles.Length * tactics.retreatDistanceScales.Length
        );

        if (!context.TryReservePlanningQueries(
                candidateCount + 1,
                candidateCount + 1,
                out int pathBudget,
                out int visibilityBudget))
        {
            repathTimer = 0f;
            KeepMovingOrStop();
            return;
        }

        // Keep the spot once it is chosen. Picking again every repath meant a
        // different corner of the fan each time, and the enemy walked a
        // circuit around the player instead of leaving.
        if (hasRetreatPoint &&
            Vector3.Distance(selfPosition, retreatPoint) >
            tactics.arrivalDistance &&
            planner.IsPointStillLeaving(
                selfPosition,
                retreatPoint,
                threats,
                ref pathBudget) &&
            TryIsSeenByAnyone(
                retreatPoint,
                ref visibilityBudget,
                out bool existingPointIsSeen) &&
            !existingPointIsSeen)
        {
            context.TryMoveTo(retreatPoint, context.Config.chaseSpeed);
            return;
        }

        switch (planner.TryFindRetreatPoint(
                    selfPosition,
                    threats,
                    ref pathBudget,
                    ref visibilityBudget,
                    out Vector3 nextRetreatPoint))
        {
            case EnemyTacticalPlanResult.Found:
                retreatPoint = nextRetreatPoint;
                hasRetreatPoint = true;
                context.TryMoveTo(retreatPoint, context.Config.chaseSpeed);
                return;

            // Half a fan searched. Keep whatever is already held and finish
            // the search next tick rather than throwing it away and starting
            // the same one again.
            case EnemyTacticalPlanResult.Deferred:
                repathTimer = 0f;
                KeepMovingOrStop();
                return;

            default:
                hasRetreatPoint = false;

                // Nothing to move to that leads away. Standing still beats
                // walking in the one direction this state exists to avoid.
                context.StopNavigation();
                return;
        }
    }

    public void Exit()
    {
        repathTimer = 0f;
        unseenTimer = 0f;
        giveUpTimer = 0f;
        hasRetreatPoint = false;
        planner.Restart();
    }

    // Everyone currently looking. When nobody is - the sighting has passed but
    // sight has not been broken for long enough yet - the target's last
    // observed pose stands in, so the enemy keeps leaving rather than stopping
    // dead for a second and a half.
    private void CollectThreats(
        IReadOnlyList<PlayerWatcher> watchers,
        Vector3 observedTargetPosition
    )
    {
        threats.Clear();

        for (int i = 0; i < watchers.Count; i++)
        {
            threats.Add(watchers[i].Position);
        }

        if (threats.Count == 0)
        {
            threats.Add(observedTargetPosition);
        }
    }

    private void KeepMovingOrStop()
    {
        if (hasRetreatPoint)
        {
            context.TryMoveTo(retreatPoint, context.Config.chaseSpeed);
            return;
        }

        context.StopNavigation();
    }

    private bool TryIsSeenByAnyone(
        Vector3 position,
        ref int visibilityBudget,
        out bool isSeen
    )
    {
        if (visibilityBudget <= 0)
        {
            isSeen = false;
            return false;
        }

        visibilityBudget--;
        isSeen = PlayerGazeNetwork.IsBodySeenByAnyone(
            position,
            context.Navigator.BodyHeight
        );
        return true;
    }
}
