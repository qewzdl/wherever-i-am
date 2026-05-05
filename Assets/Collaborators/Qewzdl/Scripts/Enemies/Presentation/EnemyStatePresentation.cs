using UnityEngine;

[System.Serializable]
public sealed class EnemyStatePresentation
{
    [SerializeField] private EnemyState state;

    [Header("Animator")]
    [SerializeField] private int animatorStateValue;
    [SerializeField] private string enterTrigger;
    [SerializeField] private bool resetTriggerOnExit;

    [Header("Enter Sound")]
    [SerializeField] private SoundEffect enterSound;
    [SerializeField] private bool playEnterSoundAtEnemyPosition = true;

    [Header("Looping Sound")]
    [SerializeField] private SoundEffect loopingSound;
    [SerializeField] private bool playLoopingSoundImmediatelyOnEnter;
    [SerializeField] private bool playLoopingSoundAtEnemyPosition = true;
    [SerializeField, Min(0.05f)] private float minLoopingSoundDelay = 2f;
    [SerializeField, Min(0.05f)] private float maxLoopingSoundDelay = 4f;

    [Header("Animation Event Sounds")]
    [SerializeField] private EnemyAnimationSound[] animationSounds;

    [Header("Threat")]
    [SerializeField] private EnemyThreatLevel threatLevel;

    public EnemyState State => state;

    public int AnimatorStateValue => animatorStateValue;
    public string EnterTrigger => enterTrigger;
    public bool ResetTriggerOnExit => resetTriggerOnExit;

    public SoundEffect EnterSound => enterSound;
    public bool PlayEnterSoundAtEnemyPosition => playEnterSoundAtEnemyPosition;

    public SoundEffect LoopingSound => loopingSound;
    public bool HasLoopingSound => loopingSound != null;
    public bool PlayLoopingSoundImmediatelyOnEnter => playLoopingSoundImmediatelyOnEnter;
    public bool PlayLoopingSoundAtEnemyPosition => playLoopingSoundAtEnemyPosition;

    public EnemyThreatLevel ThreatLevel => threatLevel;

    public float GetNextLoopingSoundDelay()
    {
        float safeMinDelay = Mathf.Max(0.05f, minLoopingSoundDelay);
        float safeMaxDelay = Mathf.Max(safeMinDelay, maxLoopingSoundDelay);

        return Random.Range(safeMinDelay, safeMaxDelay);
    }

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
        minLoopingSoundDelay = Mathf.Max(0.05f, minLoopingSoundDelay);
        maxLoopingSoundDelay = Mathf.Max(minLoopingSoundDelay, maxLoopingSoundDelay);
    }
}