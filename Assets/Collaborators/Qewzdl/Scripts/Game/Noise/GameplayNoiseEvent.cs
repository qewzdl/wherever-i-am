using UnityEngine;

public readonly struct GameplayNoiseEvent
{
    public const ulong NoNetworkObjectId = ulong.MaxValue;
    public const ulong NoClientId = ulong.MaxValue;

    public Vector3 Position { get; }
    public float Radius { get; }
    public float Loudness { get; }
    public float CreatedAtTime { get; }
    public GameplayNoiseSourceType SourceType { get; }
    public ulong SourceNetworkObjectId { get; }
    public ulong SourceClientId { get; }
    public Object SourceObject { get; }

    public bool IsValid => Radius > 0f && Loudness > 0f;
    public bool HasNetworkSource => SourceNetworkObjectId != NoNetworkObjectId;
    public bool HasClientSource => SourceClientId != NoClientId;

    public GameplayNoiseEvent(
        Vector3 position,
        float radius,
        float loudness,
        float createdAtTime,
        GameplayNoiseSourceType sourceType,
        ulong sourceNetworkObjectId,
        ulong sourceClientId,
        Object sourceObject
    )
    {
        Position = position;
        Radius = Mathf.Max(0f, radius);
        Loudness = Mathf.Max(0f, loudness);
        CreatedAtTime = createdAtTime;
        SourceType = sourceType;
        SourceNetworkObjectId = sourceNetworkObjectId;
        SourceClientId = sourceClientId;
        SourceObject = sourceObject;
    }
}