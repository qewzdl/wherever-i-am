using UnityEngine;

[System.Serializable]
public sealed class EnemyStatePresentation
{
    [SerializeField] private EnemyState state;

    [Header("Animator")]
    [SerializeField] private int animatorStateValue;
    [SerializeField] private string enterTrigger;
    [SerializeField] private bool resetTriggerOnExit;

    [Header("Enter Sounds")]
    [SerializeField] private EnemyPresentationSound[] enterSounds;

    [Header("Looping Sounds")]
    [SerializeField] private EnemyLoopingPresentationSound[] loopingSounds;

    [Header("Animation Event Sounds")]
    [SerializeField] private EnemyAnimationSound[] animationSounds;

    public EnemyState State => state;

    public int AnimatorStateValue => animatorStateValue;
    public string EnterTrigger => enterTrigger;
    public bool ResetTriggerOnExit => resetTriggerOnExit;

    public EnemyPresentationSound[] EnterSounds => enterSounds;
    public bool HasEnterSounds => enterSounds != null && enterSounds.Length > 0;

    public EnemyLoopingPresentationSound[] LoopingSounds => loopingSounds;
    public bool HasLoopingSounds => loopingSounds != null && loopingSounds.Length > 0;

    public bool TryGetAnimationSound(string eventId, out EnemyAnimationSound animationSound)
    {
        animationSound = null;

        if (animationSounds == null || string.IsNullOrWhiteSpace(eventId))
        {
            return false;
        }

        for (int i = 0; i < animationSounds.Length; i++)
        {
            EnemyAnimationSound candidate = animationSounds[i];

            if (candidate == null || !candidate.Matches(eventId))
            {
                continue;
            }

            animationSound = candidate;
            return true;
        }

        return false;
    }

    public void Normalize()
    {
        if (loopingSounds == null)
        {
            return;
        }

        for (int i = 0; i < loopingSounds.Length; i++)
        {
            loopingSounds[i]?.Normalize();
        }
    }
}