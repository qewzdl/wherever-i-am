using UnityEngine;

[CreateAssetMenu(
    fileName = "ItemImpactNoiseProfile",
    menuName = "Wherever I Am/Game/Noise/Item Impact Noise Profile")]
public sealed class ItemImpactNoiseProfile : ScriptableObject
{
    [SerializeField] private GameplayNoisePreset lightImpactNoise;
    [SerializeField] private GameplayNoisePreset mediumImpactNoise;
    [SerializeField] private GameplayNoisePreset heavyImpactNoise;
    [SerializeField] private GameplayNoisePreset landingNoise;

    public bool HasAnyNoise =>
        IsValid(lightImpactNoise) ||
        IsValid(mediumImpactNoise) ||
        IsValid(heavyImpactNoise) ||
        IsValid(landingNoise);

    public bool TryGetPreset(
        ItemImpactSoundId impactId,
        out GameplayNoisePreset preset)
    {
        preset = impactId switch
        {
            ItemImpactSoundId.LightImpact => lightImpactNoise,
            ItemImpactSoundId.MediumImpact => mediumImpactNoise,
            ItemImpactSoundId.HeavyImpact => heavyImpactNoise,
            ItemImpactSoundId.Landing => landingNoise,
            _ => null
        };

        return IsValid(preset);
    }

    private static bool IsValid(GameplayNoisePreset preset)
    {
        return preset != null && preset.IsValid;
    }
}
