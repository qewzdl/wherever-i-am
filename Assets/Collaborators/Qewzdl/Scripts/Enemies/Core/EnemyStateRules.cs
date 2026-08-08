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

    // Breaks line of sight on purpose, so losing the target is the state
    // working rather than failing. Sending these off to search the moment
    // sight is lost means they never finish what they set out to do.
    //
    // Each one carries its own timeout, so nothing waits forever.
    public static bool HandlesOwnSightLoss(EnemyState state)
    {
        return state == EnemyState.Retreat ||
               state == EnemyState.Flank ||
               state == EnemyState.Ambush;
    }
}
