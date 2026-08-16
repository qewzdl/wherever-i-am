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

    // Where the enemy stood the first frame of a sighting, which is the place
    // worth remembering: by the time the sighting has lasted long enough to
    // break off, the enemy has walked further into view.
    private Vector3 firstSeenPosition;
    private bool hasFirstSeenPosition;

    // Waiting out a room where every way round is being watched, rather than
    // walking a watched route and calling it a flank.
    private bool holdingForOpening;
    private float noOpeningTimer;
    private EnemyStealthFailureReason noOpeningReason;

    // What the players were doing when the last search ran. A search whose
    // inputs have not moved gives the same answer, so it is worth repeating
    // only when they have - or now and then, in case something the signature
    // does not cover has changed.
    private int lastGazeSignature;

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
        hasFirstSeenPosition = false;
        holdingForOpening = false;
        noOpeningTimer = 0f;
        noOpeningReason = EnemyStealthFailureReason.None;
        lastGazeSignature = context.GetRelevantGazeTopologySignature();

        EnemyEngagementTacticsRuntime engagement = context.EngagementTactics;

        // Coming round is the attempt. Counted here, where it starts, so a
        // Retreat handing back to a Flank is visible as a second try rather
        // than as more of the first one.
        engagement.NoteStealthAttempt();

        // Which way round to go. Alternating sides was only ever a guess at
        // "somewhere other than last time"; once the engagement knows which
        // side the last attempt was caught on, it is not a guess.
        if (engagement.HasSidePreference)
        {
            planner.RestartFacing(engagement.PrefersMirroredFan);
            return;
        }

        planner.Restart();
    }

    public void Tick(float deltaTime)
    {
        if (!context.TryContinueManeuver(
                out EnemyTargetObservation targetObservation))
        {
            return;
        }

        EnemyStealthTacticsConfig tactics = context.Config.StealthTactics;
        EnemyEngagementTacticsRuntime engagement = context.EngagementTactics;

        // What was learned about the corridors behind the target is about the
        // pose it had. Once it has walked off or turned round, they are
        // different corridors and the history is in the way.
        engagement.ForgetFailedRoutesIfTargetMoved(
            targetObservation.Position,
            targetObservation.Forward
        );

        // Sneaking has had its go against this person. The count survives
        // Retreat and Chase, which is the whole reason it lives on the
        // engagement rather than on the manoeuvre.
        if (engagement.HasSpentStealthAttempts(
                tactics.maxStealthAttemptsPerEngagement))
        {
            context.GiveUpOnStealth(EnemyStealthFailureReason.AttemptsSpent);
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
            if (!hasFirstSeenPosition)
            {
                firstSeenPosition = selfPosition;
                hasFirstSeenPosition = true;
            }

            seenTimer += deltaTime;

            if (seenTimer >= tactics.stalkNoticedDuration)
            {
                BreakOffAfterBeingSeen(targetObservation, tactics, engagement);
                return;
            }
        }
        else
        {
            seenTimer = 0f;
            hasFirstSeenPosition = false;
        }

        giveUpTimer += deltaTime;

        // Twelve seconds of trying to get round is long enough. Going back to
        // watching restarted the whole sequence - watch, be seen, break off,
        // fail to get round, watch again - which is the loop the log showed
        // running until the player walked away.
        if (giveUpTimer >= tactics.flankTimeout)
        {
            context.GiveUpOnStealth(EnemyStealthFailureReason.Timeout);
            return;
        }

        // The searches below are expensive and their answer only changes when
        // somebody moves or looks somewhere else. A change in the room is
        // therefore what makes one worth repeating, rather than a timer.
        int gazeSignature = context.GetRelevantGazeTopologySignature(
            targetObservation.Position
        );

        if (gazeSignature != lastGazeSignature)
        {
            lastGazeSignature = gazeSignature;
            repathTimer = 0f;
        }

        repathTimer -= deltaTime;

        // Sitting tight because there was nowhere hidden to go. The player can
        // hold this for a few seconds by watching the ways round; they cannot
        // hold it forever, and that is the deal.
        if (holdingForOpening)
        {
            noOpeningTimer += deltaTime;

            if (noOpeningTimer >= tactics.noOpeningWaitDuration)
            {
                context.GiveUpOnStealth(noOpeningReason);
                return;
            }
        }

        // Whatever the reservation below grants and the search leaves unspent.
        // Reserved queries are a claim on this frame, so the leftovers live and
        // die with this tick rather than being carried.
        int pathBudget = 0;

        if ((!hasFlankPoint && !holdingForOpening) || repathTimer <= 0f)
        {
            if (!TryPlanFlankPoint(
                    targetObservation,
                    selfPosition,
                    isSeen,
                    deltaTime,
                    ref pathBudget))
            {
                return;
            }
        }

        if (!hasFlankPoint)
        {
            return;
        }

        if (Vector3.Distance(selfPosition, flankPoint) <=
            tactics.arrivalDistance)
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
        holdingForOpening = false;
        noOpeningTimer = 0f;
        planner.ReleaseClaim();
    }

    // Seen for long enough to count. The route that got the enemy noticed, and
    // the spot it was first noticed from, are what the next attempt has to
    // avoid - remembering only the flank point sent it down the same corridor
    // to a slightly different corner and it was seen from the same doorway.
    private void BreakOffAfterBeingSeen(
        EnemyTargetObservation targetObservation,
        EnemyStealthTacticsConfig tactics,
        EnemyEngagementTacticsRuntime engagement
    )
    {
        engagement.NoteFlankExposure();
        engagement.RememberFailedRoute(
            planner.LastChosenRouteFingerprint,
            hasFirstSeenPosition ? firstSeenPosition : context.Navigator.Position,
            targetObservation.Position,
            targetObservation.Forward
        );

        // Being caught once is bad luck and worth another approach from the
        // other side. Being caught twice is the player watching the ways
        // round, and a third attempt is the same loop again.
        if (engagement.HasSpentFlankExposures(tactics.maxFlankExposureRetries))
        {
            context.GiveUpOnStealth(EnemyStealthFailureReason.Detected);
            return;
        }

        context.ChangeState(EnemyState.Retreat);
    }

    // False means the tick is over: either there was no budget to plan with
    // and nothing already held to keep walking towards, the search is only
    // part way through and will finish next tick, or there is nowhere hidden
    // to go and the enemy is waiting in cover.
    private bool TryPlanFlankPoint(
        EnemyTargetObservation targetObservation,
        Vector3 selfPosition,
        bool isSeen,
        float deltaTime,
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

        EnemyStealthPlanOutcome outcome = planner.TryFindPointBehind(
            targetObservation,
            selfPosition,
            context.EngagementTactics,
            ref pathBudget,
            ref visibilityBudget,
            out Vector3 nextFlankPoint,
            out bool pointIsUsable
        );

        switch (outcome)
        {
            case EnemyStealthPlanOutcome.FoundHiddenRoute:
                flankPoint = nextFlankPoint;
                hasFlankPoint = true;
                holdingForOpening = false;
                noOpeningTimer = 0f;
                return true;

            // Everything round is watched, and the enemy is already standing
            // in plain view. There is no stealth left for a watched route to
            // spend, so the spot behind them is still worth taking.
            case EnemyStealthPlanOutcome.AllRoutesObserved
                when pointIsUsable && isSeen:
                flankPoint = nextFlankPoint;
                hasFlankPoint = true;
                holdingForOpening = false;
                noOpeningTimer = 0f;
                return true;

            case EnemyStealthPlanOutcome.DeferredByBudget:
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

            case EnemyStealthPlanOutcome.AllRoutesObserved:
                return WaitForAnOpening(outcome, deltaTime, tactics);

            case EnemyStealthPlanOutcome.SlotOccupied:
                return WaitForAnOpening(outcome, deltaTime, tactics);

            case EnemyStealthPlanOutcome.NoReachablePoint:
                // A gaze can move and a claimed slot can be released. A wall
                // cannot. The complete fan has already established that this
                // agent cannot get behind the target, so waiting for an
                // "opening" here only delays the same Assault decision.
                context.GiveUpOnStealth(
                    EnemyStealthFailureReason.NoHiddenRoute
                );
                return false;

            case EnemyStealthPlanOutcome.OnlyPreviouslyFailedRoutes:
                // These routes exist, but this engagement has already learned
                // that they repeat the failed approach. A gaze-only wait does
                // not change that history, so escalate immediately.
                context.GiveUpOnStealth(
                    EnemyStealthFailureReason.PreviouslyFailedRoute
                );
                return false;

            default:
                context.GiveUpOnStealth(
                    EnemyStealthFailureReason.NoHiddenRoute
                );
                return false;
        }
    }

    // Nowhere hidden to go. Staying put beats walking a watched route: the
    // enemy is already out of sight, and the route is the only part of the
    // plan the players can see. Waiting costs them nothing but the seconds
    // they spend looking, and it ends in an assault either way.
    private bool WaitForAnOpening(
        EnemyStealthPlanOutcome outcome,
        float deltaTime,
        EnemyStealthTacticsConfig tactics
    )
    {
        noOpeningReason = outcome switch
        {
            EnemyStealthPlanOutcome.AllRoutesObserved =>
                EnemyStealthFailureReason.AllRoutesObserved,
            EnemyStealthPlanOutcome.SlotOccupied =>
                EnemyStealthFailureReason.SlotOccupied,
            _ => EnemyStealthFailureReason.NoHiddenRoute,
        };

        hasFlankPoint = false;
        planner.ReleaseClaim();
        context.StopNavigation();

        if (!holdingForOpening)
        {
            holdingForOpening = true;
            noOpeningTimer = deltaTime;
        }

        if (noOpeningTimer >= tactics.noOpeningWaitDuration)
        {
            context.GiveUpOnStealth(noOpeningReason);
            return false;
        }

        // Until somebody moves or looks elsewhere, the same search returns the
        // same answer. The gaze check in Tick clears this the moment they do.
        repathTimer = tactics.noOpeningReplanInterval;
        return false;
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
