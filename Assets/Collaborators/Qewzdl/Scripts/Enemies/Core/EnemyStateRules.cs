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

    // Loses sight of the target as part of doing its job, so being sent off
    // to search the moment it happens interrupts the state rather than
    // finishing it. Three of these break sight deliberately; watching counts
    // too, because a target that steps behind a door frame for a quarter
    // second has not gone anywhere, and vision only refreshes that often.
    //
    // Each one carries its own timeout, so nothing waits forever.
    public static bool HandlesOwnSightLoss(EnemyState state)
    {
        return state == EnemyState.Stalk ||
               state == EnemyState.Retreat ||
               state == EnemyState.Flank ||
               state == EnemyState.Ambush;
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
        Vector3 leg = to - from;
        leg.y = 0f;

        Vector3 toWatcher = watcher - from;
        toWatcher.y = 0f;

        float legSqrLength = leg.sqrMagnitude;

        // Not going anywhere, so not closing on anyone.
        if (legSqrLength < 0.0001f)
        {
            return false;
        }

        float travelled = Mathf.Clamp01(
            Vector3.Dot(toWatcher, leg) / legSqrLength);

        return (leg * travelled - toWatcher).sqrMagnitude <
               toWatcher.sqrMagnitude;
    }
}
