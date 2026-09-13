using UnityEngine;

[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Presentation/Enemy Presentation Profile",
    fileName = "EnemyPresentationProfile"
)]
public class EnemyPresentationProfile : ScriptableObject
{
    [Header("Animator Parameters")]
    [SerializeField] private string stateIntegerParameter = "EnemyState";
    [SerializeField] private bool useStateIntegerParameter = true;

    [SerializeField] private string attackPhaseIntegerParameter = "EnemyAttackPhase";
    [SerializeField] private bool useAttackPhaseIntegerParameter = true;

    [Header("State Presentation")]
    [SerializeField] private EnemyStatePresentation[] states;

    [Header("Heard A Noise")]
    [Tooltip(
        "Played on the enemy when a noise she acts on was loud enough to be " +
        "worth a reaction. Leave the sound empty and nothing happens.")]
    [SerializeField] private EnemyPresentationSound heardLoudNoiseSound;

    [Tooltip(
        "How loud the noise has to have been TO HER: the loudness it was made " +
        "at, after distance and after however long ago it happened. One is a " +
        "noise at full volume going off at her feet, so useful values are well " +
        "under that. Zero reacts to everything she hears.")]
    [SerializeField, Range(0f, 1f)] private float heardLoudNoiseScore = 0.35f;

    [Tooltip(
        "Seconds before she will react out loud again. A noise stays in the " +
        "world for the whole hearing memory and is re-read several times a " +
        "second, so without this one bang would set her off a dozen times.")]
    [SerializeField, Min(0f)] private float heardLoudNoiseCooldown = 6f;

    [Header("Fallback Animation Event Sounds")]
    [SerializeField] private EnemyAnimationSound[] fallbackAnimationSounds;

    public string StateIntegerParameter => stateIntegerParameter;
    public bool UseStateIntegerParameter => useStateIntegerParameter;

    public EnemyPresentationSound HeardLoudNoiseSound => heardLoudNoiseSound;
    public float HeardLoudNoiseScore => heardLoudNoiseScore;
    public float HeardLoudNoiseCooldown => heardLoudNoiseCooldown;

    public string AttackPhaseIntegerParameter => attackPhaseIntegerParameter;
    public bool UseAttackPhaseIntegerParameter => useAttackPhaseIntegerParameter;

    public bool TryGetPresentation(
        EnemyState state,
        out EnemyStatePresentation presentation
    )
    {
        presentation = null;

        if (states == null)
        {
            return false;
        }

        for (int i = 0; i < states.Length; i++)
        {
            EnemyStatePresentation candidate = states[i];

            if (candidate == null || candidate.State != state)
            {
                continue;
            }

            presentation = candidate;
            return true;
        }

        return false;
    }

    public bool TryGetAnimationSound(
        EnemyState state,
        string eventId,
        out EnemyAnimationSound animationSound
    )
    {
        animationSound = null;

        if (TryGetPresentation(state, out EnemyStatePresentation presentation) &&
            presentation.TryGetAnimationSound(eventId, out animationSound))
        {
            return true;
        }

        return TryGetFallbackAnimationSound(eventId, out animationSound);
    }

    private bool TryGetFallbackAnimationSound(
        string eventId,
        out EnemyAnimationSound animationSound
    )
    {
        animationSound = null;

        if (fallbackAnimationSounds == null || string.IsNullOrWhiteSpace(eventId))
        {
            return false;
        }

        for (int i = 0; i < fallbackAnimationSounds.Length; i++)
        {
            EnemyAnimationSound candidate = fallbackAnimationSounds[i];

            if (candidate == null || !candidate.Matches(eventId))
            {
                continue;
            }

            animationSound = candidate;
            return true;
        }

        return false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (states == null)
        {
            return;
        }

        for (int i = 0; i < states.Length; i++)
        {
            states[i]?.Normalize();
        }
    }
#endif
}