using UnityEngine;

[CreateAssetMenu(
    fileName = "RandomNoRepeatMusicSelector",
    menuName = "Game Audio/Music Selectors/Random No Repeat"
)]
public class RandomNoRepeatMusicSelector : MusicTrackSelector
{
    public override MusicTrack SelectNext(MusicTrack[] tracks, MusicSelectionState state)
    {
        if (tracks == null || tracks.Length == 0) return null;

        if (tracks.Length == 1)
        {
            return tracks[0];
        }

        MusicTrack selectedTrack;

        do
        {
            int index = Random.Range(0, tracks.Length);
            selectedTrack = tracks[index];
        }
        while (selectedTrack == state.LastTrack);

        return selectedTrack;
    }
}