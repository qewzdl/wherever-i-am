using UnityEngine;

public readonly struct GameplayNoiseQuery
{
    public float HearingRadius { get; }
    public float MemoryDuration { get; }
    public float MinimumLoudness { get; }

    public bool IsValid => HearingRadius > 0f && MemoryDuration >= 0f && MinimumLoudness >= 0f;

    public GameplayNoiseQuery(
        float hearingRadius,
        float memoryDuration,
        float minimumLoudness
    )
    {
        HearingRadius = Mathf.Max(0f, hearingRadius);
        MemoryDuration = Mathf.Max(0f, memoryDuration);
        MinimumLoudness = Mathf.Max(0f, minimumLoudness);
    }
}