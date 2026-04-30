using UnityEngine;

[CreateAssetMenu(fileName = "MusicCue", menuName = "Game Audio/Music Cue")]
public class MusicCue : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string cueId;

    [Header("Tracks")]
    [SerializeField] private MusicTrack[] tracks;

    [Header("Selection")]
    [SerializeField] private MusicTrackSelector selector;

    [Header("Playback")]
    [SerializeField] private bool continueAfterTrackEnds = false;
    [SerializeField] private bool loopCue = true;
    [SerializeField, Min(0f)] private float delayBetweenTracks = 0f;
    [SerializeField, Min(0f)] private float crossfadeBeforeTrackEnds = 1f;

    public string CueId => cueId;
    public MusicTrack[] Tracks => tracks;
    public MusicTrackSelector Selector => selector;

    public bool ContinueAfterTrackEnds => continueAfterTrackEnds;
    public bool LoopCue => loopCue;
    public float DelayBetweenTracks => delayBetweenTracks;
    public float CrossfadeBeforeTrackEnds => crossfadeBeforeTrackEnds;

    public bool IsValid => tracks != null && tracks.Length > 0;

    public MusicTrack GetFirstTrack()
    {
        if (!IsValid) return null;
        return tracks[0];
    }
}