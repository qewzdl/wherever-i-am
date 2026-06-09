using UnityEngine;

[CreateAssetMenu(
    fileName = "ItemImpactSoundProfile",
    menuName = "Wherever I Am/Audio/SFX/Items/Impact Sound Profile")]
public sealed class ItemImpactSoundProfile : ScriptableObject
{
    [Header("Impact Sounds")]
    [SerializeField] private SoundEffect lightImpactSound;
    [SerializeField] private SoundEffect mediumImpactSound;
    [SerializeField] private SoundEffect heavyImpactSound;
    [SerializeField] private SoundEffect landingSound;

    [Header("Thresholds")]
    [SerializeField, Min(0f)] private float minimumImpactSpeed = 1.25f;
    [SerializeField, Min(0f)] private float mediumImpactSpeed = 3f;
    [SerializeField, Min(0f)] private float heavyImpactSpeed = 6f;
    [SerializeField, Min(0f)] private float landingVerticalSpeed = 2.5f;
    [SerializeField, Range(0f, 1f)] private float minimumLandingNormalY = 0.35f;

    [Header("Rate Limit")]
    [SerializeField, Min(0f)] private float cooldown = 0.12f;

    public float MinimumImpactSpeed => Mathf.Max(0f, minimumImpactSpeed);
    public float MediumImpactSpeed => Mathf.Max(MinimumImpactSpeed, mediumImpactSpeed);
    public float HeavyImpactSpeed => Mathf.Max(MediumImpactSpeed, heavyImpactSpeed);
    public float LandingVerticalSpeed => Mathf.Max(0f, landingVerticalSpeed);
    public float MinimumLandingNormalY => Mathf.Clamp01(minimumLandingNormalY);
    public float Cooldown => Mathf.Max(0f, cooldown);
    public bool HasAnySound =>
        lightImpactSound != null ||
        mediumImpactSound != null ||
        heavyImpactSound != null ||
        landingSound != null;

    public bool TryResolveSound(
        float impactSpeed,
        float downwardSpeed,
        bool hasLandingContact,
        out ItemImpactSoundId soundId)
    {
        soundId = ItemImpactSoundId.None;

        if (!HasAnySound || impactSpeed < MinimumImpactSpeed)
        {
            return false;
        }

        if (hasLandingContact &&
            downwardSpeed >= LandingVerticalSpeed &&
            TryUseSound(ItemImpactSoundId.Landing, out soundId))
        {
            return true;
        }

        if (impactSpeed >= HeavyImpactSpeed &&
            TryUseSound(ItemImpactSoundId.HeavyImpact, out soundId))
        {
            return true;
        }

        if (impactSpeed >= MediumImpactSpeed &&
            TryUseSound(ItemImpactSoundId.MediumImpact, out soundId))
        {
            return true;
        }

        return TryUseSound(ItemImpactSoundId.LightImpact, out soundId);
    }

    public bool TryGetSound(
        ItemImpactSoundId soundId,
        out SoundEffect sound)
    {
        sound = soundId switch
        {
            ItemImpactSoundId.LightImpact => lightImpactSound,
            ItemImpactSoundId.MediumImpact => mediumImpactSound,
            ItemImpactSoundId.HeavyImpact => heavyImpactSound,
            ItemImpactSoundId.Landing => landingSound,
            _ => null
        };

        return sound != null;
    }

    private bool TryUseSound(
        ItemImpactSoundId candidate,
        out ItemImpactSoundId soundId)
    {
        soundId = ItemImpactSoundId.None;

        if (!TryGetSound(candidate, out _))
        {
            return false;
        }

        soundId = candidate;
        return true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        minimumImpactSpeed = Mathf.Max(0f, minimumImpactSpeed);
        mediumImpactSpeed = Mathf.Max(minimumImpactSpeed, mediumImpactSpeed);
        heavyImpactSpeed = Mathf.Max(mediumImpactSpeed, heavyImpactSpeed);
        landingVerticalSpeed = Mathf.Max(0f, landingVerticalSpeed);
        minimumLandingNormalY = Mathf.Clamp01(minimumLandingNormalY);
        cooldown = Mathf.Max(0f, cooldown);
    }
#endif
}
