using UnityEngine;

[System.Serializable]
public sealed class EnemyAnimationSound
{
    [SerializeField] private string eventId;
    [SerializeField] private SoundEffect sound;
    [SerializeField] private bool playAtEnemyPosition = true;

    public string EventId => eventId;
    public SoundEffect Sound => sound;
    public bool PlayAtEnemyPosition => playAtEnemyPosition;
    public bool IsValid => !string.IsNullOrWhiteSpace(eventId) && sound != null;

    public bool Matches(string targetEventId)
    {
        return IsValid && eventId == targetEventId;
    }
}