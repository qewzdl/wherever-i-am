public readonly struct MatchOutcome
{
    public readonly GameResultType ResultType;
    public readonly MatchResultSource Source;
    public readonly string SourceId;
    public readonly string Reason;
    public readonly ulong InstigatorClientId;

    public bool HasResult => GameResultData.IsValidResult(
        ResultType,
        Source,
        SourceId,
        InstigatorClientId);

    internal MatchOutcome(
        GameResultType resultType,
        MatchResultSource source,
        string sourceId,
        string reason,
        ulong instigatorClientId)
    {
        ResultType = resultType;
        Source = source;
        SourceId = sourceId ?? string.Empty;
        Reason = reason ?? string.Empty;
        InstigatorClientId = instigatorClientId;
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