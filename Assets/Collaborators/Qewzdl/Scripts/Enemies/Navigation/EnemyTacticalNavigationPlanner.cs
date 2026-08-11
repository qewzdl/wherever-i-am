using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum EnemyTacticalPlanResult
{
    Found = 0,

    // The whole fan was walked and the level has nothing to offer.
    NotFound = 1,

    // Out of budget partway through. Not a failure: keep whatever point is
    // already held and carry on from where the search stopped next tick.
    Deferred = 2,
}

// Where a route check stopped when it ran out of raycasts, so the next tick
// resumes there instead of walking the first corners again.
//
// Without it a route needing more samples than one frame's allowance never
// finished: every tick started at the first segment, spent the lot, and
// deferred the same candidate forever. Reachable with the shipped numbers -
// the profile asks for more route samples than the server has in a frame.
public struct EnemyRouteScan
{
    // Index of the corner the interrupted segment ends at, and the sample
    // within it. Zero means "from the start".
    public int Segment;
    public int Step;
}

// Route planning for the stealth manoeuvre, asked the way the enemy will
// actually walk it.
//
// Flank and Retreat each called NavMesh.CalculatePath and NavMesh.SamplePosition
// with NavMesh.AllAreas and the default agent type, which is not the NavMesh
// the agent moves on. Everything the navigator knows - agent type for the
// current posture, the area mask, doors, item blockers, the crawl fallback -
// was missing from the plan, so a destination could pass every check here and
// then be refused the moment EnemyNavigator was asked to go there. The enemy
// walked into a wall, gave up, and started the same failing search again.
//
// One per enemy. The route it hands back is the buffer it plans into, so it is
// read before the next plan rather than stored.
public sealed class EnemyTacticalNavigationPlanner
{
    private readonly EnemyNavigator navigator;
    private readonly NavMeshPath routePath = new();
    private readonly List<Vector3> routeCorners = new();

    public EnemyTacticalNavigationPlanner(EnemyNavigator navigator)
    {
        this.navigator = navigator;
    }

    public bool TrySamplePoint(
        Vector3 desiredPosition,
        float maximumDistance,
        out Vector3 sampledPosition
    )
    {
        sampledPosition = desiredPosition;

        return navigator != null &&
               navigator.TrySampleTacticalPoint(
                   desiredPosition,
                   maximumDistance,
                   out sampledPosition
               );
    }

    // A complete route this agent can walk, or nothing. Partial paths are the
    // reason a flank used to commit to a point on the far side of a locked
    // door.
    //
    // Spends from the caller's reserved path-query allowance, the same way
    // IsRouteHidden below spends its raycast allowance: a search that runs out
    // partway stops there and is resumed, rather than quietly running the
    // server's whole frame budget through one enemy's fan.
    public bool TryPlanRoute(
        Vector3 from,
        Vector3 to,
        ref int pathBudget,
        out IReadOnlyList<Vector3> route,
        out bool budgetExhausted
    )
    {
        routeCorners.Clear();
        route = routeCorners;
        budgetExhausted = false;

        if (navigator == null ||
            !navigator.TryPlanTacticalRoute(
                from,
                to,
                routePath,
                ref pathBudget,
                out budgetExhausted))
        {
            return false;
        }

        routeCorners.AddRange(routePath.corners);
        return routeCorners.Count > 0;
    }

    // Whether walking this route brings the enemy closer to anyone watching it
    // than it already is. Every watcher, not the one being crept up on: the
    // way out of a room is shared by everybody standing in it.
    public static bool RouteClosesOnAnyWatcher(
        IReadOnlyList<Vector3> route,
        IReadOnlyList<PlayerWatcher> watchers
    )
    {
        if (watchers == null)
        {
            return false;
        }

        for (int i = 0; i < watchers.Count; i++)
        {
            if (EnemyStateRules.RouteClosesOnWatcher(route, watchers[i].Position))
            {
                return true;
            }
        }

        return false;
    }

    // Walks the route the agent would actually take and asks whether anyone
    // could see the enemy standing anywhere along it. Sampled rather than
    // continuous, and budgeted, because the worst case lands exactly when the
    // enemy is stuck and repathing hardest.
    public bool IsRouteHidden(
        IReadOnlyList<Vector3> route,
        float bodyHeight,
        float sampleSpacing,
        ref int budget,
        ref EnemyRouteScan scan,
        out bool budgetExhausted
    )
    {
        budgetExhausted = false;

        if (route == null || route.Count < 2)
        {
            scan = default;
            return route != null && route.Count == 1;
        }

        sampleSpacing = Mathf.Max(0.25f, sampleSpacing);

        // Resume where the budget ran out last tick. The route was replanned
        // between the two, but from and to are the same and NavMesh gives the
        // same corners for them, so the count the cursor was taken against is
        // the count it is being applied to.
        //
        // ponytail: corner count is the only sanity check. A NavMesh rebuilt
        // mid-search could return a route of the same length through different
        // geometry and the skipped corners would go unchecked; add a route
        // hash if runtime rebuilds start moving geometry under a manoeuvre.
        if (scan.Segment >= route.Count)
        {
            scan = default;
        }

        int firstSegment = Mathf.Max(1, scan.Segment);
        int firstStep = Mathf.Max(1, scan.Step);

        for (int i = firstSegment; i < route.Count; i++)
        {
            Vector3 previous = route[i - 1];
            Vector3 corner = route[i];
            float length = Vector3.Distance(previous, corner);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / sampleSpacing));

            for (int step = i == firstSegment ? firstStep : 1;
                 step <= steps;
                 step++)
            {
                // Out of budget with the route half walked. Refusing is the
                // only safe answer: calling an unchecked route hidden is how
                // the enemy would walk straight across the view it was trying
                // to avoid. The cursor is what turns that refusal into
                // progress rather than a candidate that repeats forever.
                if (budget <= 0)
                {
                    budgetExhausted = true;
                    scan.Segment = i;
                    scan.Step = step;
                    return false;
                }

                budget--;

                Vector3 sample = Vector3.Lerp(
                    previous,
                    corner,
                    step / (float)steps
                );

                if (PlayerGazeNetwork.IsBodySeenByAnyone(sample, bodyHeight))
                {
                    scan = default;
                    return false;
                }
            }
        }

        scan = default;
        return true;
    }
}
