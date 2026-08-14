public static class MatchOutcomeFactory
{
    private const string PlayerCaughtSourceId = "player_caught";

    public static MatchOutcome FromPlayerCaught(
        GameResultType resultType,
        ulong caughtClientId,
        string reason)
    {
        if (resultType == GameResultType.None)
        {
            return default;
        }

        return new MatchOutcome(
            resultType,
            MatchResultSource.PlayerCaught,
            PlayerCaughtSourceId,
            reason,
            caughtClientId);
    }
}
