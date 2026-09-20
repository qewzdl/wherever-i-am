using System.Collections.Generic;
using UnityEngine;

// What the rest of the brain needs to know about a state, in one place.
//
// These two questions used to be answered by naming states inline in four
// different files - perception's no-stimulus branch, its refresh branch, the
// confirmed-target fork and the suspicious-position fork. Adding a state meant
// finding all four, and stalking, retreating, flanking and ambushing each
// shipped broken until the missing one turned up in a play session.
public static class EnemyStateRules
{
    // Working a target it already has. Anything else is treated as having no
    // business holding one, and its target is thrown away on the first frame
    // between vision refreshes.
    public static bool IsEngagedWithTarget(EnemyState state)
    {
        return state == EnemyState.Chase ||
               state == EnemyState.Attack ||
               state == EnemyState.Stalk ||
               state == EnemyState.Retreat ||
               state == EnemyState.Flank ||
               state == EnemyState.Ambush;
    }

    // The four phases of one behaviour: stop and watch, break off, come round
    // behind, wait. They are four EnemyStates because presentation needs four
    // of them; underneath they share a victim, one observation of that victim
    // and one deadline, which is what EnemyStealthManeuver holds.
    public static bool IsStealthManeuver(EnemyState state)
    {
        return state == EnemyState.Stalk ||
               state == EnemyState.Retreat ||
               state == EnemyState.Flank ||
               state == EnemyState.Ambush;
    }

    // How many steps a fallback walk may take before it is a cycle rather than
    // a chain. The chains below are two long at most and every one of them
    // ends at a state that cannot be switched off, so this bound is never
    // reached today. It is here so that an edit which accidentally points two
    // states at each other hangs a frame's worth of loop rather than the game.
    public const int FallbackChainLimit = 9;

    // What an enemy does instead when it has not been given this behaviour.
    //
    // Six of the nine states are optional, and leaving one out cannot mean the
    // brain simply refuses the transition: the request came from perception
    // noticing something, and refusing it would leave her standing in whatever
    // she was doing with the stimulus unanswered. So every optional state names
    // a plainer thing to do in its place, and the chains end at states that are
    // always installed.
    //
    //   Patrol      -> Idle        nowhere to walk, so stand.
    //   Investigate -> Patrol      nothing to search with, so carry on the
    //                              round; and on to Idle if there is no
    //                              patrolling either.
    //   Stalk, Retreat, Flank, Ambush -> Chase
    //                              no cunning available, so walk straight at
    //                              them. All four collapse to the same thing,
    //                              which is what makes the four of them one
    //                              switch rather than four.
    //
    //   Attack      -> Chase       caught them and cannot strike, so keep
    //                              running them down.
    //   Chase       -> Investigate cannot run at them, so go and look where
    //                              they are instead.
    //
    // Only standing still has none, and that is what makes it the floor rather
    // than a behaviour: a fallback has to land somewhere, and this is where
    // every chain ends. Chasing and attacking used to be down here with it, on
    // the grounds that the three were installed together. They were never the
    // same kind of thing - those two are things an enemy does, and this is what
    // is left when she is doing none of them.
    public static bool TryGetFallback(EnemyState state, out EnemyState fallback)
    {
        switch (state)
        {
            case EnemyState.Patrol:
                fallback = EnemyState.Idle;
                return true;

            // Caught up and cannot strike: keep running them down. The enemy
            // that corners you and never touches you is a design somebody may
            // want, and it should read as a configuration rather than as a
            // transition that failed.
            case EnemyState.Attack:
                fallback = EnemyState.Chase;
                return true;

            // Sees somebody and cannot run at them: go and look instead. The
            // chain continues - no investigating either, and she carries on her
            // round - which is what makes chasing something that can be left
            // out at all.
            case EnemyState.Chase:
                fallback = EnemyState.Investigate;
                return true;

            case EnemyState.Investigate:
                fallback = EnemyState.Patrol;
                return true;

            case EnemyState.Stalk:
            case EnemyState.Retreat:
            case EnemyState.Flank:
            case EnemyState.Ambush:
                fallback = EnemyState.Chase;
                return true;

            default:
                fallback = state;
                return false;
        }
    }

    // Loses sight of the target as part of doing its job, so being sent off
    // to search the moment it happens interrupts the state rather than
    // finishing it. Three of these break sight deliberately; watching counts
    // too, because a target that steps behind a door frame for a quarter
    // second has not gone anywhere, and vision only refreshes that often.
    //
    // Each one carries its own timeout, and the manoeuvre carries one over all
    // four, so nothing waits forever.
    public static bool HandlesOwnSightLoss(EnemyState state)
    {
        return IsStealthManeuver(state);
    }

    // Whether walking from one point to another would bring you closer to
    // someone than you already are.
    //
    // Comparing the two ends says nothing: backing away past someone and
    // walking round behind them both finish further off than they started,
    // and both go straight through the gap on the way. What matters is the
    // closest the walk itself comes. Flat, because a stairwell overhead is
    // not the enemy closing in.
    public static bool ClosesOnWatcher(Vector3 from, Vector3 to, Vector3 watcher)
    {
        Vector3 toWatcher = watcher - from;
        toWatcher.y = 0f;

        return ClosestApproachSqr(from, to, watcher) < toWatcher.sqrMagnitude;
    }

    // The same question asked of the walk the agent will actually take.
    //
    // A straight segment from here to there says nothing about a NavMesh route
    // between the two: the only way out of a dead end is back down the
    // corridor, and the corridor can run straight past the watcher's feet. The
    // segment test called that a retreat. Corners are what the agent follows,
    // so corners are what has to be measured.
    public static bool RouteClosesOnWatcher(
        IReadOnlyList<Vector3> routeCorners,
        Vector3 watcher
    )
    {
        if (routeCorners == null || routeCorners.Count == 0)
        {
            return false;
        }

        Vector3 toWatcher = watcher - routeCorners[0];
        toWatcher.y = 0f;

        return ClosestRouteApproachSqr(routeCorners, watcher) <
               toWatcher.sqrMagnitude;
    }

    // Whether the walk passes within a given distance of somewhere. Asked of
    // the place a flank was last spotted from: a different route through the
    // same doorway is the same approach, and doing it again gets seen again.
    public static bool RouteComesWithin(
        IReadOnlyList<Vector3> routeCorners,
        Vector3 point,
        float radius
    )
    {
        return ClosestRouteApproachSqr(routeCorners, point) <= radius * radius;
    }

    // How close the walk comes to a watcher at its nearest point, squared.
    // Flat, because a stairwell overhead is not the enemy closing in.
    private static float ClosestRouteApproachSqr(
        IReadOnlyList<Vector3> routeCorners,
        Vector3 watcher
    )
    {
        if (routeCorners == null || routeCorners.Count == 0)
        {
            return float.PositiveInfinity;
        }

        if (routeCorners.Count == 1)
        {
            Vector3 toOnlyCorner = watcher - routeCorners[0];
            toOnlyCorner.y = 0f;
            return toOnlyCorner.sqrMagnitude;
        }

        float closest = float.PositiveInfinity;

        for (int i = 1; i < routeCorners.Count; i++)
        {
            closest = Mathf.Min(
                closest,
                ClosestApproachSqr(routeCorners[i - 1], routeCorners[i], watcher)
            );
        }

        return closest;
    }

    private static float ClosestApproachSqr(
        Vector3 from,
        Vector3 to,
        Vector3 watcher
    )
    {
        Vector3 leg = to - from;
        leg.y = 0f;

        Vector3 toWatcher = watcher - from;
        toWatcher.y = 0f;

        float legSqrLength = leg.sqrMagnitude;

        // Not going anywhere, so the closest approach is where it stands.
        if (legSqrLength < 0.0001f)
        {
            return toWatcher.sqrMagnitude;
        }

        float travelled = Mathf.Clamp01(
            Vector3.Dot(toWatcher, leg) / legSqrLength);

        return (leg * travelled - toWatcher).sqrMagnitude;
    }
}
