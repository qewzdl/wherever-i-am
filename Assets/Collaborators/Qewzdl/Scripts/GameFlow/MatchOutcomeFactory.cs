public static class MatchOutcomeFactory
{
    private const string PlayerCaughtSourceId = "player_caught";

    public static MatchOutcome FromObjective(
        ObjectiveDefinition definition,
        ulong instigatorClientId)
    {
        if (definition == null || definition.ResultType == GameResultType.None)
        {
            return default;
        }

        return new MatchOutcome(
            definition.ResultType,
            MatchResultSource.Objective,
            definition.ObjectiveId,
            definition.CompletionReason,
            instigatorClientId);
    }

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
