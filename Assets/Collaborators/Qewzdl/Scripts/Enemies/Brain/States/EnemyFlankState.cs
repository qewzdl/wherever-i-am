using System.Collections.Generic;
using UnityEngine;

// Walks round to stand behind the target while it is not looking.
//
// Deliberately does not need a live target. The whole point of this state is
// to be somewhere the player cannot see, and an enemy the player cannot see
// usually cannot see the player either - the state machine log for the first
// build is full of the target being lost every few seconds. Losing it here is
// expected, so the destination uses the pose the manoeuvre observed for its
// own target. It must not silently start flanking whichever other player
// happens to be nearest, which is what a perception refresh used to be able to
// make it do.
//
// Transitions and driving the plan only. Choosing the point is
// EnemyFlankPlanner's job.
public sealed class EnemyFlankState : IEnemyStateHandler
{
    private readonly EnemyBrainContext context;
    private readonly EnemyFlankPlanner planner;

    private Vector3 flankPoint;
    private bool hasFlankPoint;
    private float repathTimer;
    private float giveUpTimer;
    private float seenTimer;

    public EnemyState State => EnemyState.Flank;

    public EnemyFlankState(EnemyBrainContext context)
    {
        this.context = context;
        planner = new EnemyFlankPlanner(context);
    }

    public void Enter()
    {
        hasFlankPoint = false;
        repathTimer = 0f;
        giveUpTimer = 0f;
        seenTimer = 0f;
        planner.Restart();
    }

    public void Tick(float deltaTime)
    {
        if (!context.TryContinueManeuver(
                out EnemyTargetObservation targetObservation))
        {
            return;
        }

        Vector3 selfPosition = context.Navigator.Position;
        IReadOnlyList<PlayerWatcher> watchers = context.GetWatchers();
        bool isSeen = watchers.Count > 0;

        // Spotted on the way round. Break off and try again from somewhere
        // else rather than walking the rest of the way in full view - but not
        // on a single frame of it. Crossing a doorway puts the enemy in view
        // for an instant, and treating that as being caught threw away the
        // whole approach every few metres.
        if (isSeen)
        {
            seenTimer += deltaTime;

            if (seenTimer >= context.Config.stalkNoticedDuration)
            {
                context.ChangeState(EnemyState.Retreat);
                return;
            }
        }
        else
        {
            seenTimer = 0f;
        }

        giveUpTimer += deltaTime;

        // Twelve seconds of trying to get round is long enough. Going back to
        // watching restarted the whole sequence - watch, be seen, break off,
        // fail to get round, watch again - which is the loop the log showed
        // running until the player walked away.
        if (giveUpTimer >= context.Config.flankTimeout)
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        repathTimer -= deltaTime;

        if (!hasFlankPoint || repathTimer <= 0f)
        {
            if (!TryPlanFlankPoint(targetObservation, selfPosition))
            {
                return;
            }
        }

        // Nowhere behind them to stand, by either standard. Sneaking is off,
        // so the honest answer is to come at them rather than go back and
        // start the same failing sequence again.
        if (!hasFlankPoint)
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        if (Vector3.Distance(selfPosition, flankPoint) <=
            context.Config.StealthTactics.arrivalDistance)
        {
            context.ChangeState(EnemyState.Ambush);
            return;
        }

        // Caught partway round. The route is allowed to be seen for stretches
        // of it - insisting otherwise found nothing in a real level - but
        // walking at someone while they are looking straight at you is not a
        // flank, it is a charge. Judged along the route the agent will take,
        // because the straight line to a point behind somebody says nothing
        // about a NavMesh route that goes round the other way. Wait them out;
        // either the sighting passes or the timer above hands this to the
        // retreat.
        if (isSeen && RouteWalksIntoAWatcher(selfPosition, watchers))
        {
            context.StopNavigation();
            return;
        }

        context.TryMoveTo(flankPoint, context.Config.chaseSpeed);
    }

    public void Exit()
    {
        hasFlankPoint = false;
        planner.ReleaseClaim();
    }

    // False means the tick is over: either there was no budget to plan with
    // and nothing already held to keep walking towards, or the search is only
    // part way through and will finish next tick.
    private bool TryPlanFlankPoint(
        EnemyTargetObservation targetObservation,
        Vector3 selfPosition
    )
    {
        EnemyStealthTacticsConfig tactics = context.Config.StealthTactics;
        int candidateCount = Mathf.Clamp(
            tactics.candidatesPerTick,
            1,
            tactics.flankBehindAngles.Length * tactics.flankDistanceScales.Length
        );

        if (!context.TryReservePlanningQueries(
                candidateCount + 1,
                candidateCount + tactics.routeSampleBudget,
                out _,
                out int visibilityBudget))
        {
            // Capacity is not a navigation failure. Keep an existing point
            // moving and retry an unplanned first point next frame.
            repathTimer = 0f;

            if (hasFlankPoint)
            {
                return true;
            }

            context.StopNavigation();
            return false;
        }

        repathTimer = tactics.flankRepathInterval;

        switch (planner.TryFindPointBehind(
                    targetObservation,
                    selfPosition,
                    ref visibilityBudget,
                    out Vector3 nextFlankPoint))
        {
            case EnemyTacticalPlanResult.Found:
                flankPoint = nextFlankPoint;
                hasFlankPoint = true;
                return true;

            case EnemyTacticalPlanResult.NotFound:
                hasFlankPoint = false;
                return true;

            default:
                repathTimer = 0f;

                if (hasFlankPoint)
                {
                    return true;
                }

                context.StopNavigation();
                return false;
        }
    }

    private bool RouteWalksIntoAWatcher(
        Vector3 selfPosition,
        IReadOnlyList<PlayerWatcher> watchers
    )
    {
        if (!context.TacticalPlanner.TryPlanRoute(
                selfPosition,
                flankPoint,
                out IReadOnlyList<Vector3> route))
        {
            // No route to judge. The straight line is the only thing left to
            // go on, and it is the conservative answer here.
            for (int i = 0; i < watchers.Count; i++)
            {
                if (EnemyStateRules.ClosesOnWatcher(
                        selfPosition,
                        flankPoint,
                        watchers[i].Position))
                {
                    return true;
                }
            }

            return false;
        }

        return EnemyTacticalNavigationPlanner.RouteClosesOnAnyWatcher(
            route,
            watchers
        );
    }
}
