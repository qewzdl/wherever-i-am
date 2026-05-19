using System;
using Unity.Netcode;
using UnityEngine;

public struct EnemyAttackPhaseSnapshot : INetworkSerializable, IEquatable<EnemyAttackPhaseSnapshot>
{
    public static readonly EnemyAttackPhaseSnapshot Idle = new(
        EnemyAttackPhase.Idle,
        EnemyTargetIdentity.None,
        default,
        default,
        EnemyAttackResultType.None,
        0d
    );

    private EnemyAttackPhase phase;
    private EnemyTargetIdentity targetIdentity;
    private Vector3 attackerPosition;
    private Vector3 targetPosition;
    private EnemyAttackResultType reason;
    private double startedServerTime;

    public EnemyAttackPhase Phase => phase;
    public EnemyTargetIdentity TargetIdentity => targetIdentity;
    public Vector3 AttackerPosition => attackerPosition;
    public Vector3 TargetPosition => targetPosition;
    public EnemyAttackResultType Reason => reason;
    public double StartedServerTime => startedServerTime;

    public bool IsActive => phase != EnemyAttackPhase.Idle;
    public bool HasTarget => targetIdentity.HasTarget;

    public EnemyAttackPhaseSnapshot(
        EnemyAttackPhase phase,
        EnemyTargetIdentity targetIdentity,
        Vector3 attackerPosition,
        Vector3 targetPosition,
        EnemyAttackResultType reason,
        double startedServerTime
    )
    {
        this.phase = phase;
        this.targetIdentity = targetIdentity;
        this.attackerPosition = attackerPosition;
        this.targetPosition = targetPosition;
        this.reason = reason;
        this.startedServerTime = Math.Max(0d, startedServerTime);
    }

    public static EnemyAttackPhaseSnapshot CreateIdle(double serverTime)
    {
        return new EnemyAttackPhaseSnapshot(
            EnemyAttackPhase.Idle,
            EnemyTargetIdentity.None,
            default,
            default,
            EnemyAttackResultType.None,
            serverTime
        );
    }

    public static EnemyAttackPhaseSnapshot FromEvent(
        EnemyAttackPhaseEvent phaseEvent,
        double serverTime
    )
    {
        return new EnemyAttackPhaseSnapshot(
            phaseEvent.Phase,
            phaseEvent.TargetIdentity,
            phaseEvent.AttackerPosition,
            phaseEvent.TargetPosition,
            phaseEvent.Reason,
            serverTime
        );
    }

    public float GetElapsedTime(double serverTime)
    {
        return Mathf.Max(0f, (float)(serverTime - startedServerTime));
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref phase);
        serializer.SerializeValue(ref targetIdentity);
        serializer.SerializeValue(ref attackerPosition);
        serializer.SerializeValue(ref targetPosition);
        serializer.SerializeValue(ref reason);
        serializer.SerializeValue(ref startedServerTime);
    }

    public bool Equals(EnemyAttackPhaseSnapshot other)
    {
        return phase == other.phase &&
               targetIdentity == other.targetIdentity &&
               attackerPosition == other.attackerPosition &&
               targetPosition == other.targetPosition &&
               reason == other.reason &&
               startedServerTime.Equals(other.startedServerTime);
    }

    public override bool Equals(object obj)
    {
        return obj is EnemyAttackPhaseSnapshot other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (int)phase;
            hash = hash * 23 + targetIdentity.GetHashCode();
            hash = hash * 23 + attackerPosition.GetHashCode();
            hash = hash * 23 + targetPosition.GetHashCode();
            hash = hash * 23 + (int)reason;
            hash = hash * 23 + startedServerTime.GetHashCode();
            return hash;
        }
    }

    public static bool operator ==(
        EnemyAttackPhaseSnapshot left,
        EnemyAttackPhaseSnapshot right
    )
    {
        return left.Equals(right);
    }

    public static bool operator !=(
        EnemyAttackPhaseSnapshot left,
        EnemyAttackPhaseSnapshot right
    )
    {
        return !left.Equals(right);
    }
}