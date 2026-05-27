using UnityEngine;

public readonly struct EnemyAttackResult
{
    public EnemyAttackResultType Type { get; }
    public EnemyTargetIdentity TargetIdentity { get; }
    public Vector3 AttackerPosition { get; }
    public Vector3 TargetPosition { get; }

    public bool WasStarted => Type == EnemyAttackResultType.Started;
    public bool WasApplied => Type == EnemyAttackResultType.Hit;
    public bool WasInterrupted => Type == EnemyAttackResultType.Interrupted ||
                                  Type == EnemyAttackResultType.OutOfRange ||
                                  Type == EnemyAttackResultType.LineOfHitBlocked ||
                                  Type == EnemyAttackResultType.InvalidTarget;

    public EnemyAttackResult(
        EnemyAttackResultType type,
        EnemyTargetIdentity targetIdentity,
        Vector3 attackerPosition,
        Vector3 targetPosition
    )
    {
        Type = type;
        TargetIdentity = targetIdentity;
        AttackerPosition = attackerPosition;
        TargetPosition = targetPosition;
    }

    public static EnemyAttackResult Create(
        EnemyAttackResultType type,
        EnemyTargetIdentity targetIdentity,
        Vector3 attackerPosition,
        Vector3 targetPosition
    )
    {
        return new EnemyAttackResult(
            type,
            targetIdentity,
            attackerPosition,
            targetPosition
        );
    }
}