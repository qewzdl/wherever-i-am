using UnityEngine;

public abstract class MusicTrackSelector : ScriptableObject
{
    public abstract MusicTrack SelectNext(MusicTrack[] tracks, MusicSelectionState state);
}

public class MusicSelectionState
{
    public int CurrentIndex { get; set; } = -1;
    public MusicTrack LastTrack { get; set; }
    public int PlayedCount { get; set; }
}