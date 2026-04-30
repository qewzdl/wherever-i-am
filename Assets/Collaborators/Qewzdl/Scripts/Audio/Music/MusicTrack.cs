using UnityEngine;

[CreateAssetMenu(fileName = "MusicTrack", menuName = "Game Audio/MusicTrack")]
public class MusicTrack : ScriptableObject 
{
    [Header("Identity")]
    [SerializeField] private string trackId;

    [Header("Audio")]
    [SerializeField] private AudioClip clip;
    [SerializeField, Range(0f, 1f)] private float volume = 1f;

    [Header("Fade")]
    [SerializeField, Min(0f)] private float fadeInTime = 1f;
    [SerializeField, Min(0f)] private float fadeOutTime = 1f;

    public string TrackId => trackId;
    public AudioClip Clip => clip;
    public float Volume => volume;
    public float FadeInTime => fadeInTime;
    public float FadeOutTime => fadeOutTime;
}
