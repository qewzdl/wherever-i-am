// Which lost connections are worth coming back from, and which are answers.
//
// The host already holds a seat for a player who drops - that is what the
// admission registry's reconnect reservation is - but nothing on the client
// ever came back to claim it, so the grace period only ever expired. This is
// the deciding half of that, kept apart from the retrying half so it can be
// read and tested as the small question it is.
internal static class NetworkReconnectPolicy
{
    // A reason means the host said something: a kick, a closed lobby, a build
    // it will not talk to. Those are decisions, and returning would either be
    // refused the same way or undo somebody's kick. Silence is the only case
    // worth retrying, and only from a session that had actually started - a
    // join that never landed is usually the wrong address, and retrying it
    // just makes the player wait longer to be told so.
    internal static bool ShouldAttempt(
        NetworkSessionState state,
        bool isServer,
        string serverReason,
        string joinAddress,
        float gracePeriodSeconds)
    {
        if (isServer)
            return false;

        if (!string.IsNullOrWhiteSpace(serverReason))
            return false;

        if (string.IsNullOrWhiteSpace(joinAddress))
            return false;

        if (gracePeriodSeconds <= 0f)
            return false;

        return state == NetworkSessionState.Lobby ||
               state == NetworkSessionState.LoadingGame ||
               state == NetworkSessionState.InGame;
    }
}
