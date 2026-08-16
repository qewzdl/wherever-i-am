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
    private float noEscapeTimer;
    private bool hasNoEscape;
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
        noEscapeTimer = 0f;
        hasNoEscape = false;
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
        EnemyStealthTacticsConfig tactics = context.Config.StealthTactics;

        giveUpTimer += deltaTime;

        // Backing off only works if there is somewhere out of sight to back
        // off to. In a lit open room with several players there is not, and
        // without this the enemy shuffled between fallback points for as long
        // as anyone kept looking at it - the state had no ending that did not
        // depend on the players' behaviour. Giving up and coming at them is
        // the same answer the flank reaches for the same reason.
        if (giveUpTimer >= context.Config.retreatTimeout)
        {
            context.GiveUpOnStealth(EnemyStealthFailureReason.Timeout);
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
            //
            // Only if the pursuit has an attempt left in it. Handing over
            // regardless let Flank enter, count the attempt it cannot have and
            // give up on its first tick, which clients saw as a frame of
            // flanking that never happened.
            if (unseenTimer >= context.Config.retreatBrokenSightDuration)
            {
                if (context.EngagementTactics.CanStartAnotherStealthAttempt(
                        tactics.maxStealthAttemptsPerEngagement))
                {
                    context.ChangeState(EnemyState.Flank);
                    return;
                }

                context.GiveUpOnStealth(
                    EnemyStealthFailureReason.AttemptsSpent
                );

                return;
            }
        }

        // The room has already been searched once with nothing out of sight in
        // it. Rooms do change - a door opens, the watcher walks on - so this
        // keeps trying, but only for a couple of seconds. Spending the whole
        // retreat timeout recomputing the same fan is what made an enemy with
        // nowhere to go shuffle in the open for eight seconds.
        //
        // Checked after the sighting has had its chance to break, because
        // getting away is the outcome this state wants and giving up is not.
        if (hasNoEscape)
        {
            noEscapeTimer += deltaTime;

            if (noEscapeTimer >= tactics.noEscapeTimeout)
            {
                context.GiveUpOnStealth(EnemyStealthFailureReason.NoEscape);
                return;
            }
        }

        repathTimer -= deltaTime;

        if (repathTimer > 0f)
        {
            return;
        }

        repathTimer = tactics.retreatRepathInterval;

        // At least one path query per candidate, because a candidate is only
        // good if the complete posture-aware route to it is.
        int candidateCount = Mathf.Clamp(
            tactics.candidatesPerTick,
            1,
            tactics.retreatAngles.Length * tactics.retreatDistanceScales.Length
        );

        // Three spare paths make four available even with one candidate: the
        // expensive posture route is standing, crawl reference, standing
        // waypoint and crawl continuation. Longer scans resume next frame.
        if (!context.TryReservePlanningQueries(
                candidateCount + 3,
                candidateCount + 1,
                out int pathBudget,
                out int visibilityBudget))
        {
            // Refused the budget, so the point in hand has not been checked
            // against where the watchers are standing now - and they are the
            // reason this state exists. Being refused is ordinary with several
            // enemies about, so this is the common case, not the corner: keep
            // the point, retry next frame, but do not walk on a verdict from
            // the last one.
            repathTimer = 0f;
            context.StopNavigation();
            return;
        }

        // Keep the spot once it is chosen. Picking again every repath meant a
        // different corner of the fan each time, and the enemy walked a
        // circuit around the player instead of leaving.
        if (hasRetreatPoint &&
            Vector3.Distance(selfPosition, retreatPoint) >
            tactics.arrivalDistance)
        {
            switch (CheckRetreatPoint(
                        ref pathBudget,
                        ref visibilityBudget))
            {
                case EnemyTacticalPlanResult.Found:
                    context.TryMoveTo(retreatPoint, context.Config.chaseSpeed);
                    return;

                // It leads back past somebody now, or nowhere at all. Drop it
                // here rather than leaving it for the search below to hand
                // back when the search itself runs out of budget.
                case EnemyTacticalPlanResult.NotFound:
                    hasRetreatPoint = false;
                    break;

                // Nothing left to check it with. It may well still be good, so
                // it is kept - but walking on an unchecked verdict is what
                // this state exists to avoid.
                default:
                    repathTimer = 0f;
                    context.StopNavigation();
                    return;
            }
        }

        switch (planner.TryFindRetreatPoint(
                    selfPosition,
                    threats,
                    ref pathBudget,
                    ref visibilityBudget,
                    out Vector3 nextRetreatPoint))
        {
            case EnemyStealthPlanOutcome.FoundHiddenRoute:
                retreatPoint = nextRetreatPoint;
                hasRetreatPoint = true;
                hasNoEscape = false;
                noEscapeTimer = 0f;
                context.TryMoveTo(retreatPoint, context.Config.chaseSpeed);
                return;

            // Half a fan searched. Anything still held got here either checked
            // or already underfoot, so it is kept and the search finishes next
            // tick rather than being thrown away and started again.
            case EnemyStealthPlanOutcome.DeferredByBudget:
                repathTimer = 0f;
                KeepMovingOrStop();
                return;

            // A whole fan searched and nowhere in it is out of sight. Worth
            // walking to anyway - it is further off - but the room has no
            // cover in it, and recomputing the same fan for the whole retreat
            // timeout is eight seconds of shuffling in the open. Give it a
            // couple of seconds, then stop pretending to sneak.
            case EnemyStealthPlanOutcome.AllRoutesObserved:
                retreatPoint = nextRetreatPoint;
                hasRetreatPoint = true;
                hasNoEscape = true;
                context.TryMoveTo(retreatPoint, context.Config.chaseSpeed);
                return;

            default:
                hasRetreatPoint = false;
                hasNoEscape = true;

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

    // Whether the point already held is still worth walking to: reachable by a
    // route that leads away, and out of sight when the enemy gets there. Both
    // halves can come back unanswered for want of budget, and unanswered is
    // its own verdict rather than a refusal.
    private EnemyTacticalPlanResult CheckRetreatPoint(
        ref int pathBudget,
        ref int visibilityBudget
    )
    {
        EnemyTacticalPlanResult leaving = planner.CheckPointStillLeaving(
            retreatPoint,
            threats,
            ref pathBudget,
            out EnemyTacticalRoute route
        );

        if (leaving != EnemyTacticalPlanResult.Found)
        {
            return leaving;
        }

        if (visibilityBudget <= 0 || route == null)
        {
            return EnemyTacticalPlanResult.Deferred;
        }

        retreatPoint = route.Endpoint;
        visibilityBudget--;

        return PlayerGazeNetwork.IsBodySeenByAnyone(
                   retreatPoint,
                   route.EndpointBodyHeight)
            ? EnemyTacticalPlanResult.NotFound
            : EnemyTacticalPlanResult.Found;
    }
}
