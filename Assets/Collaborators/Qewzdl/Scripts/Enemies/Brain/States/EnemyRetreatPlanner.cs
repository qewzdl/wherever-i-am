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
// stopped, because each candidate now costs a path query as well as a raycast
// and the frame budget is shared with every other enemy on the server.
internal sealed class EnemyRetreatPlanner
{
    private readonly EnemyBrainContext context;

    private int cursor;
    private int searchedThisPass;
    private Vector3 fallbackPoint;
    private bool hasFallbackPoint;

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
    public bool IsPointStillLeaving(
        Vector3 selfPosition,
        Vector3 point,
        IReadOnlyList<Vector3> threats
    )
    {
        return context.TacticalPlanner.TryPlanRoute(
                   selfPosition,
                   point,
                   out IReadOnlyList<Vector3> route) &&
               !RouteClosesOnAnyThreat(route, threats);
    }

    public EnemyTacticalPlanResult TryFindRetreatPoint(
        Vector3 selfPosition,
        IReadOnlyList<Vector3> threats,
        ref int visibilityBudget,
        out Vector3 point
    )
    {
        EnemyStealthTacticsConfig tactics = context.Config.StealthTactics;
        float[] angles = tactics.retreatAngles;
        float[] scales = tactics.retreatDistanceScales;
        int total = angles.Length * scales.Length;
        int allowance = Mathf.Clamp(tactics.candidatesPerTick, 1, total);

        Vector3 away = AwayFromAll(selfPosition, threats);
        float preferred = context.Config.retreatDistance;

        for (int n = 0; n < allowance; n++)
        {
            int index = (cursor + n) % total;
            float distance = preferred * scales[index / angles.Length];
            Vector3 direction =
                Quaternion.Euler(0f, angles[index % angles.Length], 0f) * away;

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
                    selfPosition,
                    candidate,
                    out IReadOnlyList<Vector3> route) ||
                RouteClosesOnAnyThreat(route, threats))
            {
                continue;
            }

            if (visibilityBudget <= 0)
            {
                break;
            }

            visibilityBudget--;

            if (!PlayerGazeNetwork.IsBodySeenByAnyone(
                    candidate,
                    context.Navigator.BodyHeight))
            {
                Restart();
                point = candidate;
                return EnemyTacticalPlanResult.Found;
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

        cursor = (cursor + allowance) % total;
        searchedThisPass += allowance;

        if (searchedThisPass < total)
        {
            point = fallbackPoint;
            return EnemyTacticalPlanResult.Deferred;
        }

        bool hadFallback = hasFallbackPoint;
        point = fallbackPoint;
        Restart();

        return hadFallback
            ? EnemyTacticalPlanResult.Found
            : EnemyTacticalPlanResult.NotFound;
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
