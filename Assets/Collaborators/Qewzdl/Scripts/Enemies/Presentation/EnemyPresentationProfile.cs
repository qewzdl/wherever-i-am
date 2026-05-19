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

    [Header("Fallback Animation Event Sounds")]
    [SerializeField] private EnemyAnimationSound[] fallbackAnimationSounds;

    public string StateIntegerParameter => stateIntegerParameter;
    public bool UseStateIntegerParameter => useStateIntegerParameter;

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