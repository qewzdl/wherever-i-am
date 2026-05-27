using UnityEngine;

public readonly struct EnemyNoiseEvent
{
    public Vector3 Position { get; }
    public float Radius { get; }
    public float Loudness { get; }
    public float CreatedAtTime { get; }
    public EnemyTarget SourceTarget { get; }
    public Object SourceObject { get; }

    public bool IsValid => Radius > 0f && Loudness > 0f;

    public EnemyNoiseEvent(
        Vector3 position,
        float radius,
        float loudness,
        float createdAtTime,
        EnemyTarget sourceTarget,
        Object sourceObject
    )
    {
        Position = position;
        Radius = Mathf.Max(0f, radius);
        Loudness = Mathf.Max(0f, loudness);
        CreatedAtTime = createdAtTime;
        SourceTarget = sourceTarget;
        SourceObject = sourceObject;
    }
}