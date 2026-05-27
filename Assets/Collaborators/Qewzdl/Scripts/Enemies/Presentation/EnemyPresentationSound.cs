using UnityEngine;

[System.Serializable]
public sealed class EnemyPresentationSound
{
    [SerializeField] private SoundEffect sound;
    [SerializeField] private bool playAtEnemyPosition = true;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float delay;
    [SerializeField, Range(0f, 1f)] private float chance = 1f;

    public SoundEffect Sound => sound;
    public bool PlayAtEnemyPosition => playAtEnemyPosition;
    public float Delay => delay;
    public float Chance => chance;
    public bool HasDelay => delay > 0f;
    public bool IsValid => sound != null && chance > 0f;

    public bool ShouldPlay()
    {
        if (!IsValid)
        {
            return false;
        }

        return chance >= 1f || Random.value <= chance;
    }
}