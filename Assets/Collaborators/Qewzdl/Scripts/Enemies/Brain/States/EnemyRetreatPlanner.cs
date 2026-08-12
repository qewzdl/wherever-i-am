using System.Collections.Generic;
using UnityEngine;

// Picks somewhere to back off to.
//
// Split out of EnemyRetreatState, which was doing transitions, NavMesh
// searching and visibility sampling at once. Two things changed on the way
// out. It plans with the enemy's own agent through EnemyTacticalNavigationPlanner
// rather than against NavMesh.AllAreas, so a point it likes is one this enemy
// can actually reach. And it judges the route rather than the straight line to
// it: the only way out of a dead end is back down the corridor the watcher is
// standing in, and a from-to segment called that a retreat.
//
// The fan is walked a few candidates per tick and resumed from where it
// stopped, because each candidate now costs one or more path queries as well
// as a raycast and the frame budget is shared with every other server enemy.
internal sealed class EnemyRetreatPlanner
{
    // How far the direction away from everyone may swing before the half of
    // the fan already walked was aimed somewhere else. The watcher list is
    // resampled eight times a second and the enemy is walking while it
    // searches, so without this a pass could mix two different rooms' worth of
    // threats and then pick the point that suited neither.
    private const float SnapshotTurnToleranceDot = 0.94f;

    // How far a watcher may drift before the half of the fan already walked
    // was judged against somebody standing somewhere else. Counting them was
    // not enough: a watcher can walk the length of a corridor without the
    // count changing, and two of them moving together barely shift the summed
    // direction away, so neither check above noticed - and the fallback held
    // over from the start of the pass was handed back as a retreat from where
    // they used to be.
    private const float ThreatMoveTolerance = 1f;

    private readonly EnemyBrainContext context;
    private readonly List<Vector3> searchThreats = new();

    private int cursor;
    private int searchedThisPass;
    private Vector3 fallbackPoint;
    private bool hasFallbackPoint;
    private Vector3 searchAway;

    public EnemyRetreatPlanner(EnemyBrainContext context)
    {
        this.context = context;
    }

    public void Restart()
    {
        cursor = 0;
        searchedThisPass = 0;
        hasFallbackPoint = false;
        fallbackPoint = default;
    }

    // Whether a point already chosen still leads away from everyone, by the
    // route the enemy would take to it. They move, and a spot that led away a
    // moment ago can lead straight back past them.
    //
    // Three answers, not two. "Could not check it" used to come back as "no
    // good", which sent the state off to search for a replacement - and a
    // search that then ran out of budget itself handed the rejected point
    // back and walked to it. Found means keep going, NotFound means drop it,
    // Deferred means nobody knows yet.
    public EnemyTacticalPlanResult CheckPointStillLeaving(
        Vector3 point,
        IReadOnlyList<Vector3> threats,
        ref int pathBudget,
        out EnemyTacticalRoute route
    )
    {
        route = null;

        if (!context.TacticalPlanner.TryPlanRoute(
                point,
                ref pathBudget,
                out route,
                out bool budgetExhausted))
        {
            return budgetExhausted
                ? EnemyTacticalPlanResult.Deferred
                : EnemyTacticalPlanResult.NotFound;
        }

        return RouteClosesOnAnyThreat(route, threats)
            ? EnemyTacticalPlanResult.NotFound
            : EnemyTacticalPlanResult.Found;
    }

    // Three endings rather than two. Somewhere out of sight, somewhere that
    // only puts distance between the enemy and the people looking at it, and
    // nothing at all - and the state waits out the second, because the room
    // may open up, but not the third.
    public EnemyStealthPlanOutcome TryFindRetreatPoint(
        Vector3 selfPosition,
        IReadOnlyList<Vector3> threats,
        ref int pathBudget,
        ref int visibilityBudget,
        out Vector3 point
    )
    {
        EnemyStealthTacticsConfig tactics = context.Config.StealthTactics;
        float[] angles = tactics.retreatAngles;
        float[] scales = tactics.retreatDistanceScales;
        int total = angles.Length * scales.Length;

        Vector3 away = AwayFromAll(selfPosition, threats);

        // One set of watchers per pass. Somebody joining, leaving or walking
        // changes what "away" means, and half a fan aimed at the old answer is
        // not a search of the new one.
        if (searchedThisPass > 0 &&
            (!IsSameThreats(threats) ||
             Vector3.Dot(away, searchAway) < SnapshotTurnToleranceDot))
        {
            Restart();
        }

        if (searchedThisPass == 0)
        {
            searchAway = away;
            searchThreats.Clear();

            // Copied by index rather than AddRange: the list arrives as an
            // interface, and that overload boxes an enumerator every pass.
            for (int i = 0; threats != null && i < threats.Count; i++)
            {
                searchThreats.Add(threats[i]);
            }
        }

        // Only what is left of the fan. Taking the whole per-tick allowance on
        // the last tick of a pass wrapped round and judged the first few
        // candidates a second time, at the server's expense.
        int allowance = Mathf.Clamp(
            tactics.candidatesPerTick,
            1,
            total - searchedThisPass
        );

        float preferred = context.Config.retreatDistance;

        // Only the candidates actually seen through to an answer move the
        // cursor. Stepping it by the whole allowance meant a tick that spent
        // its budget on the first candidate marked the rest as searched.
        int completed = 0;

        for (int n = 0; n < allowance; n++)
        {
            if (visibilityBudget <= 0 || pathBudget <= 0)
            {
                break;
            }

            completed = n + 1;

            int index = (cursor + n) % total;
            float distance = preferred * scales[index / angles.Length];
            Vector3 direction =
                Quaternion.Euler(0f, angles[index % angles.Length], 0f) *
                searchAway;

            if (!context.TacticalPlanner.TrySamplePoint(
                    selfPosition + direction * distance,
                    distance * 0.5f,
                    out Vector3 candidate))
            {
                continue;
            }

            // The wide angles swing round the watchers rather than away from
            // them, and at these distances they can finish on the far side -
            // further off than they started, having walked right through the
            // gap on the way.
            if (!context.TacticalPlanner.TryPlanRoute(
                    candidate,
                    ref pathBudget,
                    out EnemyTacticalRoute route,
                    out bool pathBudgetExhausted))
            {
                // No route, or no budget left to ask for one. Only the first
                // is a verdict on this candidate.
                if (pathBudgetExhausted)
                {
                    completed = n;
                    break;
                }

                continue;
            }

            // Where the route ends, not where it was aimed. Planning samples
            // the destination again with the navigator's own radius, which is
            // wider than the distance that counts as having arrived, so the
            // point judged out of sight has to be the point walked to. Every
            // check below this line is on the endpoint, and it costs nothing
            // extra here because they all come after the route.
            candidate = route.Endpoint;

            if (RouteClosesOnAnyThreat(route, threats))
            {
                continue;
            }

            visibilityBudget--;

            if (!PlayerGazeNetwork.IsBodySeenByAnyone(
                    candidate,
                    route.EndpointBodyHeight))
            {
                Restart();
                point = candidate;
                return EnemyStealthPlanOutcome.FoundHiddenRoute;
            }

            // Nowhere out of sight yet. Remember the first reachable spot so
            // the enemy keeps moving instead of standing in the open waiting
            // for a perfect answer this room may not have.
            if (!hasFallbackPoint)
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
            return EnemyStealthPlanOutcome.DeferredByBudget;
        }

        bool hadFallback = hasFallbackPoint;
        point = fallbackPoint;
        Restart();

        // A reachable spot that is still in somebody's view is worth walking
        // to - it is further away, and distance is most of what breaking off
        // is - but it is not the outcome this state exists for, and the state
        // starts counting how long it has been settling for it.
        return hadFallback
            ? EnemyStealthPlanOutcome.AllRoutesObserved
            : EnemyStealthPlanOutcome.NoReachablePoint;
    }

    // ponytail: compared by index. The watcher list comes back in registration
    // order, so a reorder is unlikely and costs only a restarted search when
    // it happens; match on owner id if it turns out to churn.
    private bool IsSameThreats(IReadOnlyList<Vector3> threats)
    {
        int count = threats != null ? threats.Count : 0;

        if (count != searchThreats.Count)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            if ((threats[i] - searchThreats[i]).sqrMagnitude >
                ThreatMoveTolerance * ThreatMoveTolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static bool RouteClosesOnAnyThreat(
        IReadOnlyList<Vector3> route,
        IReadOnlyList<Vector3> threats
    )
    {
        if (threats == null)
        {
            return false;
        }

        for (int i = 0; i < threats.Count; i++)
        {
            if (EnemyStateRules.RouteClosesOnWatcher(route, threats[i]))
            {
                return true;
            }
        }

        return false;
    }

    // Away from the crowd, not away from one of them. Backing off from the
    // player being stalked while a second one stands behind you is how the
    // enemy walked into the person who had spotted it.
    private static Vector3 AwayFromAll(
        Vector3 selfPosition,
        IReadOnlyList<Vector3> threats
    )
    {
        Vector3 away = Vector3.zero;

        if (threats != null)
        {
            for (int i = 0; i < threats.Count; i++)
            {
                Vector3 fromThreat = selfPosition - threats[i];
                fromThreat.y = 0f;

                // Standing exactly on one of them is not a direction.
                if (fromThreat.sqrMagnitude > 0.01f)
                {
                    away += fromThreat.normalized;
                }
            }
        }

        // Nobody to move away from, or they surround the enemy evenly and
        // cancel out. Anywhere will do; the visibility check still applies.
        if (away.sqrMagnitude < 0.01f)
        {
            return Vector3.forward;
        }

        return away.normalized;
    }
}
