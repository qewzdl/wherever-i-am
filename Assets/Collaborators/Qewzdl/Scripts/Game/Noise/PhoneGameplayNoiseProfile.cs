using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct PhoneGameplayNoiseEntry
{
    [SerializeField] private PhoneAudioCueType cueType;
    [SerializeField] private GameplayNoisePreset preset;

    public PhoneAudioCueType CueType => cueType;
    public GameplayNoisePreset Preset => preset;
}

[CreateAssetMenu(
    fileName = "PhoneGameplayNoiseProfile",
    menuName = "Wherever I Am/Game/Noise/Phone Gameplay Noise Profile")]
public sealed class PhoneGameplayNoiseProfile : ScriptableObject
{
    [SerializeField] private List<PhoneGameplayNoiseEntry> entries = new();

    public bool TryGetPreset(
        PhoneAudioCueType cueType,
        out GameplayNoisePreset preset)
    {
        preset = null;

        if (cueType == PhoneAudioCueType.Unknown)
        {
            return false;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            PhoneGameplayNoiseEntry entry = entries[i];

            if (entry.CueType != cueType)
            {
                continue;
            }

            preset = entry.Preset;
            return preset != null && preset.IsValid;
        }

        return false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        HashSet<PhoneAudioCueType> configuredCueTypes = new();

        for (int i = 0; i < entries.Count; i++)
        {
            PhoneGameplayNoiseEntry entry = entries[i];

            if (entry.CueType == PhoneAudioCueType.Unknown)
            {
                Debug.LogError(
                    $"{nameof(PhoneGameplayNoiseProfile)} '{name}' contains " +
                    $"an entry with {nameof(PhoneAudioCueType.Unknown)} cue type.",
                    this
                );

                continue;
            }

            if (!configuredCueTypes.Add(entry.CueType))
            {
                Debug.LogError(
                    $"{nameof(PhoneGameplayNoiseProfile)} '{name}' contains " +
                    $"duplicate '{entry.CueType}' entries.",
                    this
                );
            }

            if (entry.Preset == null || !entry.Preset.IsValid)
            {
                Debug.LogError(
                    $"{nameof(PhoneGameplayNoiseProfile)} '{name}' has no valid " +
                    $"{nameof(GameplayNoisePreset)} for '{entry.CueType}'.",
                    this
                );
            }
        }
    }
#endif
}
