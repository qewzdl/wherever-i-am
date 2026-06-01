public struct MatchOutcome
{
    public GameResultType ResultType;
    public string Reason;
    public string SourceId;
    public ulong InstigatorClientId;

    public bool HasResult => ResultType != GameResultType.None;

    public static MatchOutcome Create(
        GameResultType resultType,
        string reason,
        string sourceId,
        ulong instigatorClientId)
    {
        return new MatchOutcome
        {
            ResultType = resultType,
            Reason = reason ?? string.Empty,
            SourceId = sourceId ?? string.Empty,
            InstigatorClientId = instigatorClientId
        };
    }

    public GameResultData ToGameResultData()
    {
        return GameResultData.Create(
            ResultType,
            Reason,
            SourceId,
            InstigatorClientId);
    }
}