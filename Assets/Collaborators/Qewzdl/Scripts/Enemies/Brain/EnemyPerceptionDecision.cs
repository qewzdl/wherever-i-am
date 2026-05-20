public readonly struct EnemyPerceptionDecision
{
    public static readonly EnemyPerceptionDecision None = new(
        EnemyPerceptionDecisionType.None
    );

    public EnemyPerceptionDecisionType Type { get; }

    public bool HasDecision => Type != EnemyPerceptionDecisionType.None;

    private EnemyPerceptionDecision(EnemyPerceptionDecisionType type)
    {
        Type = type;
    }

    public static EnemyPerceptionDecision ConfirmedTarget()
    {
        return new EnemyPerceptionDecision(EnemyPerceptionDecisionType.ConfirmedTarget);
    }

    public static EnemyPerceptionDecision SuspiciousPosition()
    {
        return new EnemyPerceptionDecision(EnemyPerceptionDecisionType.SuspiciousPosition);
    }
}