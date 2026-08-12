using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public readonly struct EnemyTacticalRouteLeg
{
    public EnemyPosture Posture { get; }
    public float BodyHeight { get; }
    public int FirstCornerIndex { get; }
    public int CornerCount { get; }
    public Vector3 Endpoint { get; }

    internal EnemyTacticalRouteLeg(
        EnemyPosture posture,
        float bodyHeight,
        int firstCornerIndex,
        int cornerCount,
        Vector3 endpoint)
    {
        Posture = posture;
        BodyHeight = bodyHeight;
        FirstCornerIndex = firstCornerIndex;
        CornerCount = cornerCount;
        Endpoint = endpoint;
    }
}

// Immutable for the duration of one tactical query. The owning planner reuses
// its buffers on the next query, so callers consume the route immediately and
// store only its endpoint. Keeping the posture legs beside the flattened
// corners lets visibility use the body height the enemy will have there.
public sealed class EnemyTacticalRoute : IReadOnlyList<Vector3>
{
    private const float SharedCornerSqrTolerance = 0.0025f;

    private readonly List<Vector3> corners = new();
    private readonly List<EnemyTacticalRouteLeg> legs = new();

    public int Count => corners.Count;
    public Vector3 this[int index] => corners[index];
    public IReadOnlyList<EnemyTacticalRouteLeg> Legs => legs;
    public Vector3 Endpoint => corners.Count > 0
        ? corners[corners.Count - 1]
        : default;
    public float EndpointBodyHeight => legs.Count > 0
        ? legs[legs.Count - 1].BodyHeight
        : 0f;

    internal int Fingerprint { get; private set; }

    internal void Clear()
    {
        corners.Clear();
        legs.Clear();
        Fingerprint = 0;
    }

    internal bool CopyFrom(
        EnemyPostureTraversalPlan plan,
        EnemyNavigator navigator)
    {
        Clear();

        for (int legIndex = 0; legIndex < plan.LegCount; legIndex++)
        {
            EnemyPostureTraversalLeg sourceLeg = plan.GetLeg(legIndex);
            Vector3[] sourceCorners = sourceLeg.Path?.corners;

            if (sourceCorners == null || sourceCorners.Length == 0)
            {
                Clear();
                return false;
            }

            int firstCornerIndex = corners.Count == 0
                ? 0
                : corners.Count - 1;
            int sourceStart = 0;

            if (corners.Count > 0 &&
                (corners[corners.Count - 1] - sourceCorners[0]).sqrMagnitude <=
                SharedCornerSqrTolerance)
            {
                sourceStart = 1;
            }

            for (int i = sourceStart; i < sourceCorners.Length; i++)
            {
                corners.Add(sourceCorners[i]);
            }

            int cornerCount = corners.Count - firstCornerIndex;

            if (cornerCount <= 0)
            {
                Clear();
                return false;
            }

            legs.Add(new EnemyTacticalRouteLeg(
                sourceLeg.Posture,
                navigator.GetBodyHeightForPosture(sourceLeg.Posture),
                firstCornerIndex,
                cornerCount,
                sourceLeg.Destination));
        }

        Fingerprint = CalculateFingerprint();
        return corners.Count > 0 && legs.Count == plan.LegCount;
    }

    internal float GetBodyHeightForSegment(int segmentEndCornerIndex)
    {
        for (int i = 0; i < legs.Count; i++)
        {
            EnemyTacticalRouteLeg leg = legs[i];
            int firstSegmentEnd = leg.FirstCornerIndex + 1;
            int lastSegmentEnd = leg.FirstCornerIndex + leg.CornerCount - 1;

            if (segmentEndCornerIndex >= firstSegmentEnd &&
                segmentEndCornerIndex <= lastSegmentEnd)
            {
                return leg.BodyHeight;
            }
        }

        return EndpointBodyHeight;
    }

    public IEnumerator<Vector3> GetEnumerator()
    {
        return corners.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private int CalculateFingerprint()
    {
        unchecked
        {
            int hash = corners.Count;

            for (int i = 0; i < corners.Count; i++)
            {
                Vector3 corner = corners[i];
                hash = hash * 31 + Mathf.RoundToInt(corner.x * 4f);
                hash = hash * 31 + Mathf.RoundToInt(corner.y * 4f);
                hash = hash * 31 + Mathf.RoundToInt(corner.z * 4f);
            }

            for (int i = 0; i < legs.Count; i++)
            {
                EnemyTacticalRouteLeg leg = legs[i];
                hash = hash * 31 + (int)leg.Posture;
                hash = hash * 31 + Mathf.RoundToInt(leg.BodyHeight * 100f);
                hash = hash * 31 + leg.FirstCornerIndex;
                hash = hash * 31 + leg.CornerCount;
            }

            return hash;
        }
    }
}

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

    // The route those two were counted against. Resuming on a different route
    // skips corners nobody looked at, and calling an unchecked stretch hidden
    // is the one answer this must never give.
    public int Fingerprint;
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
    private readonly EnemyTacticalRoute routePlan = new();

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
    // Always from where the enemy is standing. The builder samples its own
    // feet and asks whether it can hold the posture there, so an origin passed
    // in would have been a different question from the one being answered.
    public bool TryPlanRoute(
        Vector3 to,
        ref int pathBudget,
        out EnemyTacticalRoute route,
        out bool budgetExhausted
    )
    {
        routePlan.Clear();
        route = routePlan;
        budgetExhausted = false;

        if (navigator == null ||
            !navigator.TryPlanTacticalRoute(
                to,
                ref pathBudget,
                out EnemyPostureTraversalPlan posturePlan,
                out budgetExhausted))
        {
            return false;
        }

        return routePlan.CopyFrom(posturePlan, navigator);
    }

    // Cheap identity for a route, quantised to a quarter of a metre so the
    // float noise of replanning the same route does not read as a new one.
    // Routes are a handful of corners, so this is a few multiplies next to the
    // raycasts it protects.
    private static int Fingerprint(IReadOnlyList<Vector3> route)
    {
        int hash = route.Count;

        for (int i = 0; i < route.Count; i++)
        {
            Vector3 corner = route[i];
            hash = hash * 31 + Mathf.RoundToInt(corner.x * 4f);
            hash = hash * 31 + Mathf.RoundToInt(corner.y * 4f);
            hash = hash * 31 + Mathf.RoundToInt(corner.z * 4f);
        }

        return hash;
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
        return IsRouteHidden(
            route,
            null,
            bodyHeight,
            sampleSpacing,
            ref budget,
            ref scan,
            out budgetExhausted);
    }

    public bool IsRouteHidden(
        EnemyTacticalRoute route,
        float sampleSpacing,
        ref int budget,
        ref EnemyRouteScan scan,
        out bool budgetExhausted
    )
    {
        return IsRouteHidden(
            route,
            route,
            route != null ? route.EndpointBodyHeight : 0f,
            sampleSpacing,
            ref budget,
            ref scan,
            out budgetExhausted);
    }

    private static bool IsRouteHidden(
        IReadOnlyList<Vector3> route,
        EnemyTacticalRoute postureRoute,
        float defaultBodyHeight,
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

        // Resume where the budget ran out last tick, but only down the route
        // the cursor was counted against. The route is replanned every tick,
        // and a rebuilt NavMesh or a moved obstacle can hand back a different
        // one with the same number of corners - resuming into that skips a
        // stretch nobody looked at and reports it hidden.
        int fingerprint = postureRoute != null
            ? postureRoute.Fingerprint
            : Fingerprint(route) * 31 +
              Mathf.RoundToInt(defaultBodyHeight * 100f);

        if (scan.Fingerprint != fingerprint || scan.Segment >= route.Count)
        {
            scan = default;
            scan.Fingerprint = fingerprint;
        }

        int firstSegment = Mathf.Max(1, scan.Segment);
        int firstStep = Mathf.Max(1, scan.Step);

        for (int i = firstSegment; i < route.Count; i++)
        {
            Vector3 previous = route[i - 1];
            Vector3 corner = route[i];
            float bodyHeight = postureRoute != null
                ? postureRoute.GetBodyHeightForSegment(i)
                : defaultBodyHeight;
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
