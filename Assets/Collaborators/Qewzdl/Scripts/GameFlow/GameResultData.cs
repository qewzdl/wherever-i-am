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

    public bool HasResult => IsValidResult(ResultType, Source, SourceId, InstigatorClientId);

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

    public static bool IsValidResult(
        GameResultType resultType,
        MatchResultSource source,
        string sourceId,
        ulong instigatorClientId)
    {
        if (resultType == GameResultType.None || source == MatchResultSource.None)
        {
            return false;
        }

        switch (source)
        {
            case MatchResultSource.Objective:
                return !string.IsNullOrWhiteSpace(sourceId);

            case MatchResultSource.Timeout:
                return true;

            case MatchResultSource.Admin:
                return true;

            case MatchResultSource.Disconnect:
                return !string.IsNullOrWhiteSpace(sourceId);

            case MatchResultSource.PlayerCaught:
                return true;

            default:
                return false;
        }
    }

    public static bool IsValidResult(
        GameResultType resultType,
        MatchResultSource source,
        FixedString64Bytes sourceId,
        ulong instigatorClientId)
    {
        return IsValidResult(
            resultType,
            source,
            sourceId.ToString(),
            instigatorClientId);
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