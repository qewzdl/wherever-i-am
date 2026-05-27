using UnityEngine;

[CreateAssetMenu(
    fileName = "SequentialMusicSelector",
    menuName = "Wherever I Am/Audio/Music/Selectors/Sequential"
)]
public class SequentialMusicSelector : MusicTrackSelector
{
    public override MusicTrack SelectNext(MusicTrack[] tracks, MusicSelectionState state)
    {
        if (tracks == null || tracks.Length == 0) return null;

        state.CurrentIndex++;

        if (state.CurrentIndex >= tracks.Length)
        {
            state.CurrentIndex = 0;
        }

        return tracks[state.CurrentIndex];
    }
}
