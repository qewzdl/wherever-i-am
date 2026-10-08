#if UNITY_EDITOR || DEVELOPMENT_BUILD
// What the developer window may ask of the local player. The window lives in
// the player assembly and the rules for dying and spawning live with the
// match, so whoever owns those rules implements this.
public interface IDeveloperPlayerActions
{
    // Ends this player's part in the match, as being caught does. Host only.
    bool TryDie(out string reason);

    // Puts this player back on their spawn point.
    bool TryReturnToSpawn(out string reason);
}
#endif
