using System.Collections.Generic;
using UnityEngine;

// Picks somewhere behind the target to come to rest.
//
// Split out of EnemyFlankState, which had grown to four jobs in one file:
// transitions, NavMesh searching, visibility sampling and reserving the spot
// against other enemies. The spot reservation now lives in
// EnemyTacticalSlotRegistry and the routes come from the enemy's own agent
// through EnemyTacticalNavigationPlanner, so a point this likes is one the
// navigator will accept.
//
// Straight behind first, then wider round either side, then closer in. A point
// directly behind is often against a wall; the fan gives the level a chance to
// offer something reachable that is still out of view.
internal sealed class EnemyFlankPlanner
{
    // How far the target may drift, and how far it may turn, before the pose
    // the search started against is a different pose. Past either, the half of
    // the fan already walked was judged against something that is no longer
    // true, so the search starts again rather than mixing the two.
    private const float SnapshotMoveTolerance = 1f;
    private const float SnapshotTurnToleranceDot = 0.94f;

    private readonly EnemyBrainContext context;

    private int cursor;
    private int searchedThisPass;
    private bool mirrorSides;
    private Vector3 fallbackPoint;
    private bool hasFallbackPoint;
    private Vector3 searchTargetPosition;
    private Vector3 searchTargetForward;

    // Where the enemy stood when the pass began. Routes are planned from where
    // it stands now - the builder samples its own feet, so there is no other
    // origin to give it - and this is the anchor that says when "now" has
    // moved far enough that the half of the fan already judged was judged from
    // somewhere else. The state stands the enemy still while a pass is in
    // flight, so in practice the two stay together and a half-finished route
    // check resumes down the same route.
    private Vector3 searchSelfPosition;

    // Which candidate the half-finished route check belongs to, so a stale
    // cursor is never applied to the next candidate's route.
    private int routeScanCandidate = -1;
    private EnemyRouteScan routeScan;

    // Whether a pass is part way through. Everything it has judged so far was
    // judged from where the enemy stood when it began, so letting the enemy
    // walk while it waits for budget throws the pass away.
    //
    // A half-walked route counts even when no candidate has been finished yet.
    // The first candidate's route can be long enough to spend the whole tick,
    // which leaves the cursor holding a place in it and the candidate count at
    // zero - and that was read as "nothing in progress", so the enemy walked
    // on and the scan it had already paid for was thrown away.
    public bool HasPendingSearch =>
        searchedThisPass > 0 || routeScanCandidate >= 0;

    public EnemyFlankPlanner(EnemyBrainContext context)
    {
        this.context = context;
    }

    // Swap which way round the fan is tried on every attempt. The order used
    // to be fixed, so an approach that failed on the left was retried on the
    // left, and the enemy kept walking into the same blocked corner.
    public void Restart()
    {
        mirrorSides = !mirrorSides;
        ResetSearch();
    }

    public EnemyTacticalPlanResult TryFindPointBehind(
        EnemyTargetObservation targetObservation,
        Vector3 selfPosition,
        ref int pathBudget,
        ref int visibilityBudget,
        out Vector3 point
    )
    {
        EnemyStealthTacticsConfig tactics = context.Config.StealthTactics;
        float[] angles = tactics.flankBehindAngles;
        float[] scales = tactics.flankDistanceScales;
        int total = angles.Length * scales.Length;

        // One pose per pass. Perception refreshes the observation whenever it
        // sees the target again, so without this the first half of the fan was
        // judged against where the target stood two ticks ago and the second
        // half against where it stands now - and the point finally chosen
        // could be the one left over from the older of the two.
        //
        // The enemy's own position is part of that pose: it is walking to the
        // point it already had while it looks for the next one, and every
        // candidate is judged by the route from where it stands.
        if (HasPendingSearch &&
            (!IsSameSearchPose(targetObservation) ||
             (selfPosition - searchSelfPosition).sqrMagnitude >
             SnapshotMoveTolerance * SnapshotMoveTolerance))
        {
            ResetSearch();
        }

        // A route check part way through counts as a pass in progress even
        // before its candidate is finished, or the origin its corners were
        // measured from would be moved out from under it every tick.
        if (!HasPendingSearch)
        {
            searchTargetPosition = targetObservation.Position;
            searchTargetForward = targetObservation.Forward;
            searchSelfPosition = selfPosition;
        }

        // Only what is left of the fan. Taking the whole per-tick allowance on
        // the last tick of a pass wrapped round and judged the first few
        // candidates a second time - twenty-four checks for a fan of
        // twenty-one, and the extra three paid for on the server.
        int allowance = Mathf.Clamp(
            tactics.candidatesPerTick,
            1,
            total - searchedThisPass
        );

        Vector3 behind = -searchTargetForward;
        behind.y = 0f;

        if (behind.sqrMagnitude < 0.001f)
        {
            behind = Vector3.forward;
        }

        behind.Normalize();

        float preferred = context.Config.flankBehindDistance;
        // How many of this tick's candidates were seen through to an answer.
        // Advancing the cursor by the whole allowance regardless was how a
        // budget spent entirely on one long route left seven points behind
        // it marked as searched and never looked at again.
        int completed = 0;

        for (int n = 0; n < allowance; n++)
        {
            // Nothing left to judge a candidate with. Stop on the one that has
            // not been judged yet, so next tick resumes on it.
            //
            // One raycast allowance for both the endpoint and the route.
            // Counting them separately let a tick spend the endpoint checks on
            // top of a full route budget, which is more raycasts than the
            // scheduler ever granted.
            if (visibilityBudget <= 0 || pathBudget <= 0)
            {
                break;
            }

            completed = n + 1;

            int index = (cursor + n) % total;
            float distance = preferred * scales[index / angles.Length];
            float angle = angles[index % angles.Length];

            if (mirrorSides)
            {
                angle = -angle;
            }

            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * behind;

            if (!context.TacticalPlanner.TrySamplePoint(
                    searchTargetPosition + direction * distance,
                    distance * 0.5f,
                    out Vector3 candidate))
            {
                continue;
            }

            // Reachable first, by this agent's own type, area mask and
            // posture, so "reachable" means reachable by this enemy rather
            // than by an idealised one.
            if (!context.TacticalPlanner.TryPlanRoute(
                    candidate,
                    ref pathBudget,
                    out EnemyTacticalRoute route,
                    out bool pathBudgetExhausted))
            {
                if (pathBudgetExhausted)
                {
                    completed = n;
                    break;
                }

                continue;
            }

            // The route ends where the agent can stand, which is not always
            // the point asked for: planning samples the destination again with
            // the navigator's radius rather than this fan's, and the posture
            // that finds a way through need not be the one that found the
            // point. So the view is asked about this, once, after the route -
            // asking first and adopting the endpoint afterwards left the enemy
            // holding a verdict about a spot it was no longer going to.
            //
            // The loop above only starts a candidate with a raycast in hand,
            // and nothing else spends one before here.
            candidate = route.Endpoint;
            visibilityBudget--;

            // No use walking to a spot somebody is already looking at. Anybody,
            // not just the one being crept up on: a route hidden from one
            // player, walked in full view of another, is how the enemy was
            // spotted, broke off, and started the same circling again.
            if (PlayerGazeNetwork.IsBodySeenByAnyone(
                    candidate,
                    route.EndpointBodyHeight))
            {
                continue;
            }

            // Nor by a route that crosses somebody's view on the way. The
            // shortest path to somewhere behind a person usually goes straight
            // past their face, which is how an enemy circling a player who
            // never moved kept walking back into sight and starting over.
            EnemyRouteScan scan = routeScanCandidate == index
                ? routeScan
                : default;

            bool routeHidden = context.TacticalPlanner.IsRouteHidden(
                route,
                tactics.routeSampleSpacing,
                ref visibilityBudget,
                ref scan,
                out bool routeBudgetExhausted
            );

            // Half a route checked is no verdict on this candidate either.
            // Where it stopped is kept, so next tick carries on down the same
            // route rather than re-walking the corners it already paid for -
            // a route longer than one tick's allowance would otherwise never
            // reach an answer.
            if (routeBudgetExhausted)
            {
                routeScanCandidate = index;
                routeScan = scan;
                completed = n;
                break;
            }

            routeScanCandidate = -1;

            if (routeHidden)
            {
                if (EnemyTacticalSlotRegistry.TryClaim(
                        context.TacticalOwnerId,
                        candidate,
                        tactics.claimSpacing))
                {
                    ResetSearch();
                    point = candidate;
                    return EnemyTacticalPlanResult.Found;
                }

                continue;
            }

            // A hidden place to stand, reached by a route that is seen for
            // part of the way, still ends with the enemy behind the player.
            // Insisting on a wholly hidden route found nothing at all in a
            // real level - the log for that build is Flank falling straight
            // back to Stalk over and over - so it is preferred, not required.
            if (!hasFallbackPoint &&
                !EnemyTacticalSlotRegistry.IsClaimedByAnother(
                    context.TacticalOwnerId,
                    candidate,
                    tactics.claimSpacing))
            {
                hasFallbackPoint = true;
                fallbackPoint = candidate;
            }
        }

        cursor = (cursor + completed) % total;
        searchedThisPass += completed;

        if (searchedThisPass < total)
        {
            point = fallbackPoint;
            return EnemyTacticalPlanResult.Deferred;
        }

        // The fallback was free when it was found, several ticks ago. Whether
        // it still is decides this, not what was true then.
        bool claimed = hasFallbackPoint &&
                       EnemyTacticalSlotRegistry.TryClaim(
                           context.TacticalOwnerId,
                           fallbackPoint,
                           context.Config.StealthTactics.claimSpacing);

        point = fallbackPoint;
        ResetSearch();

        return claimed
            ? EnemyTacticalPlanResult.Found
            : EnemyTacticalPlanResult.NotFound;
    }

    public void ReleaseClaim()
    {
        EnemyTacticalSlotRegistry.Release(context.TacticalOwnerId);
    }

    private bool IsSameSearchPose(EnemyTargetObservation observation)
    {
        return (observation.Position - searchTargetPosition).sqrMagnitude <
               SnapshotMoveTolerance * SnapshotMoveTolerance &&
               Vector3.Dot(observation.Forward, searchTargetForward) >
               SnapshotTurnToleranceDot;
    }

    private void ResetSearch()
    {
        cursor = 0;
        searchedThisPass = 0;
        hasFallbackPoint = false;
        fallbackPoint = default;
        routeScanCandidate = -1;
        routeScan = default;
    }
}
