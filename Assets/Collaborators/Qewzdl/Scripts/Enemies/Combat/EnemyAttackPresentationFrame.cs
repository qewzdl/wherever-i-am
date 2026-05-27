using System;
using Unity.Netcode;
using UnityEngine;

public struct EnemyAttackPresentationFrame : INetworkSerializable, IEquatable<EnemyAttackPresentationFrame>
{
    public EnemyAttackPhase Phase;
    public EnemyTargetIdentity TargetIdentity;
    public Vector3 AttackerPosition;
    public Vector3 TargetPosition;
    public EnemyAttackResultType Reason;
    public uint SequenceId;

    public bool HasValue => SequenceId != 0;

    public static EnemyAttackPresentationFrame Empty => new EnemyAttackPresentationFrame
    {
        Phase = EnemyAttackPhase.Idle,
        TargetIdentity = EnemyTargetIdentity.None,
        AttackerPosition = default,
        TargetPosition = default,
        Reason = EnemyAttackResultType.None,
        SequenceId = 0
    };

    public static EnemyAttackPresentationFrame FromPhaseEvent(
        EnemyAttackPhaseEvent phaseEvent,
        uint sequenceId
    )
    {
        return new EnemyAttackPresentationFrame
        {
            Phase = phaseEvent.Phase,
            TargetIdentity = phaseEvent.TargetIdentity,
            AttackerPosition = phaseEvent.AttackerPosition,
            TargetPosition = phaseEvent.TargetPosition,
            Reason = phaseEvent.Reason,
            SequenceId = sequenceId
        };
    }

    public EnemyAttackPhaseEvent ToPhaseEvent()
    {
        return new EnemyAttackPhaseEvent(
            Phase,
            TargetIdentity,
            AttackerPosition,
            TargetPosition,
            Reason
        );
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref Phase);
        serializer.SerializeValue(ref TargetIdentity);
        serializer.SerializeValue(ref AttackerPosition);
        serializer.SerializeValue(ref TargetPosition);
        serializer.SerializeValue(ref Reason);
        serializer.SerializeValue(ref SequenceId);
    }

    public bool Equals(EnemyAttackPresentationFrame other)
    {
        return Phase == other.Phase &&
               TargetIdentity.Equals(other.TargetIdentity) &&
               AttackerPosition.Equals(other.AttackerPosition) &&
               TargetPosition.Equals(other.TargetPosition) &&
               Reason == other.Reason &&
               SequenceId == other.SequenceId;
    }

    public override bool Equals(object obj)
    {
        return obj is EnemyAttackPresentationFrame other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hashCode = (int)Phase;
            hashCode = (hashCode * 397) ^ TargetIdentity.GetHashCode();
            hashCode = (hashCode * 397) ^ AttackerPosition.GetHashCode();
            hashCode = (hashCode * 397) ^ TargetPosition.GetHashCode();
            hashCode = (hashCode * 397) ^ (int)Reason;
            hashCode = (hashCode * 397) ^ SequenceId.GetHashCode();
            return hashCode;
        }
    }
}