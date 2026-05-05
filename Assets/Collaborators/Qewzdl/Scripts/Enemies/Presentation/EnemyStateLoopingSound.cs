using UnityEngine;

[System.Serializable]
public sealed class EnemyStateLoopingSound
{
    [SerializeField] private EnemyState state;
    [SerializeField] private SoundEffect sound;

    [Header("Timing")]
    [SerializeField] private bool playImmediatelyOnEnter;
    [SerializeField, Min(0.05f)] private float minDelay = 2f;
    [SerializeField, Min(0.05f)] private float maxDelay = 4f;

    [Header("Playback")]
    [SerializeField] private bool playAtEnemyPosition = true;

    public EnemyState State => state;
    public SoundEffect Sound => sound;
    public bool PlayImmediatelyOnEnter => playImmediatelyOnEnter;
    public bool PlayAtEnemyPosition => playAtEnemyPosition;
    public bool IsValid => sound != null;

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