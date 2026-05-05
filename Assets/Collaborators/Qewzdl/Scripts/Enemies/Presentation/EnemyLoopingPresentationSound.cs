using UnityEngine;

[System.Serializable]
public sealed class EnemyLoopingPresentationSound
{
    [SerializeField] private SoundEffect sound;
    [SerializeField] private bool playAtEnemyPosition = true;

    [Header("Timing")]
    [SerializeField] private bool playImmediatelyOnEnter;
    [SerializeField, Min(0.05f)] private float minDelay = 2f;
    [SerializeField, Min(0.05f)] private float maxDelay = 4f;

    [Header("Random")]
    [SerializeField, Range(0f, 1f)] private float chance = 1f;

    public SoundEffect Sound => sound;
    public bool PlayAtEnemyPosition => playAtEnemyPosition;
    public bool PlayImmediatelyOnEnter => playImmediatelyOnEnter;
    public bool IsValid => sound != null && chance > 0f;

    public bool ShouldPlay()
    {
        if (!IsValid)
        {
            return false;
        }

        return chance >= 1f || Random.value <= chance;
    }

    public float GetNextDelay()
    {
        float safeMinDelay = Mathf.Max(0.05f, minDelay);
        float safeMaxDelay = Mathf.Max(safeMinDelay, maxDelay);

        return Random.Range(safeMinDelay, safeMaxDelay);
    }

    public void Normalize()
    {
        minDelay = Mathf.Max(0.05f, minDelay);
        maxDelay = Mathf.Max(minDelay, maxDelay);
    }
}