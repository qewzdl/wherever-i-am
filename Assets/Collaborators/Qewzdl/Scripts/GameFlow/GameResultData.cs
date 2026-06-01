using System;
using Unity.Collections;
using Unity.Netcode;

public struct GameResultData : INetworkSerializable, IEquatable<GameResultData>
{
    public GameResultType ResultType;
    public MatchResultSource Source;
    public FixedString64Bytes SourceId;
    public FixedString128Bytes Reason;
    public ulong InstigatorClientId;

    public bool HasResult => ResultType != GameResultType.None && Source != MatchResultSource.None;

    public static GameResultData None => new GameResultData
    {
        ResultType = GameResultType.None,
        Source = MatchResultSource.None,
        SourceId = string.Empty,
        Reason = string.Empty,
        InstigatorClientId = 0
    };

    public static GameResultData Create(
        GameResultType resultType,
        MatchResultSource source,
        string sourceId,
        string reason,
        ulong instigatorClientId)
    {
        return new GameResultData
        {
            ResultType = resultType,
            Source = source,
            SourceId = sourceId ?? string.Empty,
            Reason = reason ?? string.Empty,
            InstigatorClientId = instigatorClientId
        };
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ResultType);
        serializer.SerializeValue(ref Source);
        serializer.SerializeValue(ref SourceId);
        serializer.SerializeValue(ref Reason);
        serializer.SerializeValue(ref InstigatorClientId);
    }

    public bool Equals(GameResultData other)
    {
        return ResultType == other.ResultType
               && Source == other.Source
               && SourceId.Equals(other.SourceId)
               && Reason.Equals(other.Reason)
               && InstigatorClientId == other.InstigatorClientId;
    }
}