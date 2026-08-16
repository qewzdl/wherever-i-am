using UnityEngine;

public readonly struct GameplayNoiseQuery
{
    public float HearingRadius { get; }
    public float HearingSensitivity { get; }
    public float MemoryDuration { get; }
    public float MinimumLoudness { get; }

    public bool IsValid => HearingRadius > 0f && MemoryDuration >= 0f && MinimumLoudness >= 0f;

    public GameplayNoiseQuery(
        float hearingRadius,
        float memoryDuration,
        float minimumLoudness,
        float hearingSensitivity = 1f
    )
    {
        HearingRadius = Mathf.Max(0f, hearingRadius);
        HearingSensitivity = Mathf.Max(0.01f, hearingSensitivity);
        MemoryDuration = Mathf.Max(0f, memoryDuration);
        MinimumLoudness = Mathf.Max(0f, minimumLoudness);
    }
}