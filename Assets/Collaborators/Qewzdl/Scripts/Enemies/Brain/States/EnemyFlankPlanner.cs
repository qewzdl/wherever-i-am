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

    // Which candidate the half-finished route check belongs to, so a stale
    // cursor is never applied to the next candidate's route.
    private int routeScanCandidate = -1;
    private EnemyRouteScan routeScan;

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
        int allowance = Mathf.Clamp(tactics.candidatesPerTick, 1, total);

        // One pose per pass. Perception refreshes the observation whenever it
        // sees the target again, so without this the first half of the fan was
        // judged against where the target stood two ticks ago and the second
        // half against where it stands now - and the point finally chosen
        // could be the one left over from the older of the two.
        //
        // ponytail: pose only. A rebuilt NavMesh mid-search still mixes
        // topologies; add a revision check if runtime rebuilds start moving
        // geometry under a live manoeuvre.
        if (searchedThisPass > 0 &&
            !IsSameSearchPose(targetObservation))
        {
            ResetSearch();
        }

        if (searchedThisPass == 0)
        {
            searchTargetPosition = targetObservation.Position;
            searchTargetForward = targetObservation.Forward;
        }

        Vector3 behind = -searchTargetForward;
        behind.y = 0f;

        if (behind.sqrMagnitude < 0.001f)
        {
            behind = Vector3.forward;
        }

        behind.Normalize();

        float preferred = context.Config.flankBehindDistance;
        float bodyHeight = context.Navigator.BodyHeight;

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

            visibilityBudget--;

            // No use walking to a spot the player is already looking at.
            // Hidden from everyone, not just the one being crept up on.
            // Checking only the nearest meant the enemy planned a route
            // hidden from one player, walked it in full view of another, was
            // spotted, broke off and started over - the same circling as
            // before, brought on by a second person in the room.
            if (PlayerGazeNetwork.IsBodySeenByAnyone(candidate, bodyHeight))
            {
                continue;
            }

            // Nor to a hidden spot the enemy cannot get to. Asked with this
            // agent's own type, area mask and posture, so "reachable" means
            // reachable by this enemy rather than by an idealised one.
            if (!context.TacticalPlanner.TryPlanRoute(
                    selfPosition,
                    candidate,
                    ref pathBudget,
                    out IReadOnlyList<Vector3> route,
                    out bool pathBudgetExhausted))
            {
                if (pathBudgetExhausted)
                {
                    completed = n;
                    break;
                }

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
                bodyHeight,
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
