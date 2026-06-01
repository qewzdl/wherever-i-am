public struct MatchOutcome
{
    public GameResultType ResultType;
    public MatchResultSource Source;
    public string SourceId;
    public string Reason;
    public ulong InstigatorClientId;

    public bool HasResult => ResultType != GameResultType.None && Source != MatchResultSource.None;

    public static MatchOutcome Create(
        GameResultType resultType,
        MatchResultSource source,
        string sourceId,
        string reason,
        ulong instigatorClientId)
    {
        return new MatchOutcome
        {
            ResultType = resultType,
            Source = source,
            SourceId = sourceId ?? string.Empty,
            Reason = reason ?? string.Empty,
            InstigatorClientId = instigatorClientId
        };
    }

    public GameResultData ToGameResultData()
    {
        return GameResultData.Create(
            ResultType,
            Source,
            SourceId,
            Reason,
            InstigatorClientId);
    }
}