using System.Globalization;

public static class MatchOutcomeFactory
{
    private const string TimeoutSourceId = "timeout";
    private const string AdminSourceId = "admin";
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

    public static MatchOutcome FromTimeout(
        GameResultType resultType,
        string reason)
    {
        return FromSystemSource(
            resultType,
            MatchResultSource.Timeout,
            TimeoutSourceId,
            reason);
    }

    public static MatchOutcome FromAdmin(
        GameResultType resultType,
        string reason,
        ulong adminClientId)
    {
        if (resultType == GameResultType.None)
        {
            return default;
        }

        return new MatchOutcome(
            resultType,
            MatchResultSource.Admin,
            AdminSourceId,
            reason,
            adminClientId);
    }

    public static MatchOutcome FromDisconnect(
        GameResultType resultType,
        ulong disconnectedClientId,
        string reason)
    {
        if (resultType == GameResultType.None)
        {
            return default;
        }

        return new MatchOutcome(
            resultType,
            MatchResultSource.Disconnect,
            disconnectedClientId.ToString(CultureInfo.InvariantCulture),
            reason,
            disconnectedClientId);
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

    private static MatchOutcome FromSystemSource(
        GameResultType resultType,
        MatchResultSource source,
        string sourceId,
        string reason)
    {
        if (resultType == GameResultType.None || source == MatchResultSource.None)
        {
            return default;
        }

        return new MatchOutcome(
            resultType,
            source,
            sourceId,
            reason,
            0);
    }
}