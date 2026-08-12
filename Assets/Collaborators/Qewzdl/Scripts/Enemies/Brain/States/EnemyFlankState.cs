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

        // Whatever the reservation below grants and the search leaves unspent.
        // Reserved queries are a claim on this frame, so the leftovers live and
        // die with this tick rather than being carried.
        int pathBudget = 0;

        if (!hasFlankPoint || repathTimer <= 0f)
        {
            if (!TryPlanFlankPoint(
                    targetObservation,
                    selfPosition,
                    ref pathBudget))
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
        if (isSeen &&
            RouteWalksIntoAWatcher(selfPosition, watchers, ref pathBudget))
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
        Vector3 selfPosition,
        ref int pathBudget
    )
    {
        EnemyStealthTacticsConfig tactics = context.Config.StealthTactics;
        int candidateCount = Mathf.Clamp(
            tactics.candidatesPerTick,
            1,
            tactics.flankBehindAngles.Length * tactics.flankDistanceScales.Length
        );

        // Endpoints and routes share one raycast allowance, and asking for
        // more of it than the whole server has in a frame is not a bigger
        // budget - it is a request the scheduler silently truncates, so the
        // search believed it had samples nobody granted. A search too big for
        // one tick now resumes mid-route instead.
        int visibilityQueries = candidateCount + Mathf.Max(
            1,
            Mathf.Min(
                tactics.routeSampleBudget,
                EnemyServerPerceptionScheduler.VisibilityQueriesPerFrame -
                candidateCount
            )
        );

        // One route per candidate plus three spare queries. Four is the minimum
        // complete allowance for the expensive case: standing attempt, crawl
        // reference, standing waypoint and crawl continuation. A larger fan
        // naturally grants more; a long waypoint scan still resumes.
        if (!context.TryReservePlanningQueries(
                candidateCount + 3,
                visibilityQueries,
                out pathBudget,
                out int visibilityBudget))
        {
            // Capacity is not a navigation failure. Keep an existing point
            // moving and retry an unplanned first point next frame - unless a
            // pass is already part way through, because everything it has
            // judged so far was judged from where the enemy stood when it
            // began. Walking on until the budget comes back moves that spot,
            // and the pass starts over: under load, a long route check would
            // begin again and again and never finish.
            repathTimer = 0f;

            if (hasFlankPoint && !planner.HasPendingSearch)
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
                    ref pathBudget,
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

                // Stand still until the pass finishes, even with a point in
                // hand. Every candidate in a pass is judged by the route from
                // where the enemy stood when the pass began - that is what
                // lets a half-finished route check resume - so walking on
                // while it runs would have it commit to a route from a place
                // it has left, which in a room with a pillar in it is the
                // other way round the pillar. A deferred pass resumes on the
                // very next frame, so this is a few frames of not moving,
                // and only when the first candidates all failed.
                context.StopNavigation();
                return false;
        }
    }

    private bool RouteWalksIntoAWatcher(
        Vector3 selfPosition,
        IReadOnlyList<PlayerWatcher> watchers,
        ref int pathBudget
    )
    {
        // Four queries cover a standing prefix plus its crawl continuation.
        // Most ticks do not plan, so there is nothing left over to spend and
        // this asks the server for its own.
        if (pathBudget <= 0 &&
            context.TryReservePlanningQueries(
                4,
                0,
                out int grantedPathQueries,
                out _))
        {
            pathBudget = grantedPathQueries;
        }

        // No route means the enemy has no business walking, whichever reason
        // it was: a point it cannot reach is not one it can walk to, and a
        // check it could not afford is not a verdict. The straight line used
        // to stand in for both and is conservative for neither - a real route
        // bends round obstacles, sometimes towards the very watcher this is
        // asking about, and reading that as "not walking into anyone" is how
        // the enemy kept going. Standing still costs a tick; the flank timeout
        // hands this to the chase if it never resolves.
        if (!context.TacticalPlanner.TryPlanRoute(
                flankPoint,
                ref pathBudget,
                out EnemyTacticalRoute route,
                out _))
        {
            return true;
        }

        return EnemyTacticalNavigationPlanner.RouteClosesOnAnyWatcher(
            route,
            watchers
        );
    }
}
