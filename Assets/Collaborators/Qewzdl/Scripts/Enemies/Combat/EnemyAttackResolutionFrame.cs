using System;
using Unity.Netcode;
using UnityEngine;

public struct EnemyAttackResolutionFrame : INetworkSerializable, IEquatable<EnemyAttackResolutionFrame>
{
    public EnemyAttackResultType ResultType;
    public EnemyTargetIdentity TargetIdentity;
    public Vector3 AttackerPosition;
    public Vector3 TargetPosition;
    public uint SequenceId;

    public bool HasValue => SequenceId != 0;

    public static EnemyAttackResolutionFrame FromResult(
        EnemyAttackResult result,
        uint sequenceId
    )
    {
        return new EnemyAttackResolutionFrame
        {
            ResultType = result.Type,
            TargetIdentity = result.TargetIdentity,
            AttackerPosition = result.AttackerPosition,
            TargetPosition = result.TargetPosition,
            SequenceId = sequenceId
        };
    }

    public EnemyAttackResult ToResult()
    {
        return EnemyAttackResult.Create(
            ResultType,
            TargetIdentity,
            AttackerPosition,
            TargetPosition
        );
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref ResultType);
        serializer.SerializeValue(ref TargetIdentity);
        serializer.SerializeValue(ref AttackerPosition);
        serializer.SerializeValue(ref TargetPosition);
        serializer.SerializeValue(ref SequenceId);
    }

    public bool Equals(EnemyAttackResolutionFrame other)
    {
        return ResultType == other.ResultType &&
               TargetIdentity.Equals(other.TargetIdentity) &&
               AttackerPosition.Equals(other.AttackerPosition) &&
               TargetPosition.Equals(other.TargetPosition) &&
               SequenceId == other.SequenceId;
    }

    public override bool Equals(object obj)
    {
        return obj is EnemyAttackResolutionFrame other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hashCode = (int)ResultType;
            hashCode = (hashCode * 397) ^ TargetIdentity.GetHashCode();
            hashCode = (hashCode * 397) ^ AttackerPosition.GetHashCode();
            hashCode = (hashCode * 397) ^ TargetPosition.GetHashCode();
            hashCode = (hashCode * 397) ^ SequenceId.GetHashCode();
            return hashCode;
        }
    }
}