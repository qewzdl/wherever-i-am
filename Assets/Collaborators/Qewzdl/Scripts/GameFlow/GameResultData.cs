using System;
using Unity.Collections;
using Unity.Netcode;

public struct GameResultData : INetworkSerializable, IEquatable<GameResultData>
{
    public GameResultType ResultType;
    public FixedString128Bytes Reason;
    public FixedString64Bytes ObjectiveId;
    public ulong InstigatorClientId;

    public static GameResultData None => new GameResultData
    {
        ResultType = GameResultType.None,
        Reason = string.Empty,
        ObjectiveId = string.Empty,
        InstigatorClientId = 0
    };

    public static GameResultData Create(
        GameResultType resultType,
        string reason,
        string objectiveId,
        ulong instigatorClientId)
    {
        return new GameResultData
        {
            ResultType = resultType,
            Reason = reason ?? string.Empty,
            ObjectiveId = objectiveId ?? string.Empty,
            InstigatorClientId = instigatorClientId
        };
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ResultType);
        serializer.SerializeValue(ref Reason);
        serializer.SerializeValue(ref ObjectiveId);
        serializer.SerializeValue(ref InstigatorClientId);
    }

    public bool Equals(GameResultData other)
    {
        return ResultType == other.ResultType
               && Reason.Equals(other.Reason)
               && ObjectiveId.Equals(other.ObjectiveId)
               && InstigatorClientId == other.InstigatorClientId;
    }
}