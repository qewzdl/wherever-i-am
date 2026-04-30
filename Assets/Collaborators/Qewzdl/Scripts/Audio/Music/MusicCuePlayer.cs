using System.Collections;
using UnityEngine;

public class MusicCuePlayer : MonoBehaviour
{
    [Header("Cue")]
    [SerializeField] private MusicCue cue;

    [Header("Playback")]
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private bool stopOnDisable = false;
    [SerializeField] private bool restartIfSameTrack = false;

    private Coroutine playRoutine;
    private MusicSelectionState state;

    private void Start()
    {
        if (playOnStart)
        {
            Play();
        }
    }

    private void OnDisable()
    {
        if (stopOnDisable)
        {
            Stop();
        }
    }

    public void Play()
    {
        if (cue == null || !cue.IsValid)
        {
            Debug.LogWarning("MusicCuePlayer: MusicCue is missing or empty.");
            return;
        }

        if (AudioManager.Instance == null || AudioManager.Instance.Music == null)
        {
            Debug.LogWarning("MusicCuePlayer: AudioManager or MusicManager is missing.");
            return;
        }

        StopRoutine();

        state = new MusicSelectionState();
        playRoutine = StartCoroutine(PlayCueRoutine());
    }

    public void Stop()
    {
        StopRoutine();

        if (AudioManager.Instance == null || AudioManager.Instance.Music == null) return;

        AudioManager.Instance.Music.StopMusic();
    }

    private IEnumerator PlayCueRoutine()
    {
        while (true)
        {
            MusicTrack track = GetNextTrack();

            if (track == null || track.Clip == null)
            {
                Debug.LogWarning("MusicCuePlayer: Track or AudioClip is missing.");
                yield break;
            }

            bool shouldRestartIfSameTrack = restartIfSameTrack || state.PlayedCount > 0;
            AudioManager.Instance.Music.PlayTrack(track, shouldRestartIfSameTrack);

            state.LastTrack = track;
            state.PlayedCount++;

            if (!ShouldScheduleNextTrack())
            {
                playRoutine = null;
                yield break;
            }

            float waitTime = Mathf.Max(
                0f,
                track.Clip.length - cue.CrossfadeBeforeTrackEnds
            );

            yield return new WaitForSecondsRealtime(waitTime);

            if (cue.DelayBetweenTracks > 0f)
            {
                yield return new WaitForSecondsRealtime(cue.DelayBetweenTracks);
            }
        }
    }

    private MusicTrack GetNextTrack()
    {
        if (cue.Selector == null)
        {
            return cue.GetFirstTrack();
        }

        return cue.Selector.SelectNext(cue.Tracks, state);
    }

    private bool ShouldScheduleNextTrack()
    {
        if (cue.LoopCue)
        {
            return true;
        }

        if (!cue.ContinueAfterTrackEnds || cue.Selector == null)
        {
            return false;
        }

        return state.PlayedCount < cue.Tracks.Length;
    }

    private void StopRoutine()
    {
        if (playRoutine == null) return;

        StopCoroutine(playRoutine);
        playRoutine = null;
    }
}
