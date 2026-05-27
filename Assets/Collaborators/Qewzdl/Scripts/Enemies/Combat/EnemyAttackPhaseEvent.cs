using UnityEngine;

public readonly struct EnemyAttackPhaseEvent
{
    public EnemyAttackPhase Phase { get; }
    public EnemyTargetIdentity TargetIdentity { get; }
    public Vector3 AttackerPosition { get; }
    public Vector3 TargetPosition { get; }
    public EnemyAttackResultType Reason { get; }

    public EnemyAttackPhaseEvent(
        EnemyAttackPhase phase,
        EnemyTargetIdentity targetIdentity,
        Vector3 attackerPosition,
        Vector3 targetPosition,
        EnemyAttackResultType reason = EnemyAttackResultType.None
    )
    {
        Phase = phase;
        TargetIdentity = targetIdentity;
        AttackerPosition = attackerPosition;
        TargetPosition = targetPosition;
        Reason = reason;
    }
}