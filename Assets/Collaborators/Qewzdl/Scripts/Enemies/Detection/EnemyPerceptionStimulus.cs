using UnityEngine;

// A stimulus reports what a sensor perceived and nothing more. Which hiding
// place a player used is never perceivable in a single sample - an entry can
// complete inside one call - so that inference lives in EnemyPerceptionRuntime,
// which can compare ticks, rather than being smuggled through here.
public readonly struct EnemyPerceptionStimulus
{
    public static readonly EnemyPerceptionStimulus None = new(
        false,
        null,
        default,
        0f,
        EnemyPerceptionSource.None,
        false,
        GameplayNoiseSourceType.Unknown
    );

    public bool HasStimulus { get; }
    public EnemyTarget Target { get; }
    public Vector3 Position { get; }
    public float Score { get; }
    public EnemyPerceptionSource Source { get; }
    public bool IsConfirmedTarget { get; }

    // What made the sound, when hearing is what perceived it. Unknown for
    // everything else, which is the honest answer: sight does not report a
    // kind of noise.
    public GameplayNoiseSourceType NoiseSource { get; }

    public bool HasTarget => Target != null;

    private EnemyPerceptionStimulus(
        bool hasStimulus,
        EnemyTarget target,
        Vector3 position,
        float score,
        EnemyPerceptionSource source,
        bool isConfirmedTarget,
        GameplayNoiseSourceType noiseSource
    )
    {
        HasStimulus = hasStimulus;
        Target = target;
        Position = position;
        Score = score;
        Source = source;
        IsConfirmedTarget = isConfirmedTarget;
        NoiseSource = noiseSource;
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
            true,
            GameplayNoiseSourceType.Unknown
        );
    }

    public static EnemyPerceptionStimulus ForSuspiciousPosition(
        Vector3 position,
        float score,
        EnemyPerceptionSource source
    )
    {
        return ForSuspiciousPosition(
            position,
            score,
            source,
            GameplayNoiseSourceType.Unknown);
    }

    public static EnemyPerceptionStimulus ForSuspiciousPosition(
        Vector3 position,
        float score,
        EnemyPerceptionSource source,
        GameplayNoiseSourceType noiseSource
    )
    {
        return new EnemyPerceptionStimulus(
            true,
            null,
            position,
            score,
            source,
            false,
            noiseSource
        );
    }
}
