using UnityEngine;

public readonly struct EnemyPerceptionStimulus
{
    public static readonly EnemyPerceptionStimulus None = new(
        false,
        null,
        default,
        0f,
        EnemyPerceptionSource.None,
        false
    );

    public bool HasStimulus { get; }
    public EnemyTarget Target { get; }
    public Vector3 Position { get; }
    public float Score { get; }
    public EnemyPerceptionSource Source { get; }
    public bool IsConfirmedTarget { get; }

    public bool HasTarget => Target != null;

    private EnemyPerceptionStimulus(
        bool hasStimulus,
        EnemyTarget target,
        Vector3 position,
        float score,
        EnemyPerceptionSource source,
        bool isConfirmedTarget
    )
    {
        HasStimulus = hasStimulus;
        Target = target;
        Position = position;
        Score = score;
        Source = source;
        IsConfirmedTarget = isConfirmedTarget;
    }

    public static EnemyPerceptionStimulus ForConfirmedTarget(
        EnemyTarget target,
        Vector3 position,
        float score,
        EnemyPerceptionSource source
    )
    {
        if (target == null)
        {
            return None;
        }

        return new EnemyPerceptionStimulus(
            true,
            target,
            position,
            score,
            source,
            true
        );
    }

    public static EnemyPerceptionStimulus ForSuspiciousPosition(
        Vector3 position,
        float score,
        EnemyPerceptionSource source
    )
    {
        return new EnemyPerceptionStimulus(
            true,
            null,
            position,
            score,
            source,
            false
        );
    }
}