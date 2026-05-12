using UnityEngine;

[CreateAssetMenu(
    fileName = "RandomMusicSelector",
    menuName = "Wherever I Am/Audio/Music/Selectors/Random"
)]
public class RandomMusicSelector : MusicTrackSelector
{
    public override MusicTrack SelectNext(MusicTrack[] tracks, MusicSelectionState state)
    {
        if (tracks == null || tracks.Length == 0) return null;

        int index = Random.Range(0, tracks.Length);
        return tracks[index];
    }
}
