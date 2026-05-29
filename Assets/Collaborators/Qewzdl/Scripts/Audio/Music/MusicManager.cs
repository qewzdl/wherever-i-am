using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

public class MusicManager : MonoBehaviour
{
    [Header("Mixer")]
    [SerializeField] private AudioMixerGroup musicMixerGroup;

    [Header("Settings")]
    [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;
    [SerializeField] private bool useUnscaledTime = true;

    private AudioSource currentSource;
    private AudioSource nextSource;

    private MusicTrack currentTrack;
    private MusicCue currentCue;

    private Coroutine transitionCoroutine;
    private Coroutine cueRoutine;

    private MusicSelectionState cueState;

    public MusicTrack CurrentTrack => currentTrack;
    public MusicCue CurrentCue => currentCue;
    public bool IsPlaying => currentSource != null && currentSource.isPlaying;

    private void Awake()
    {
        currentSource = CreateAudioSource("Current Music Source");
        nextSource = CreateAudioSource("Next Music Source");
    }

    public void PlayCue(MusicCue cue, bool restartIfSameCue = false)
    {
        if (cue == null || !cue.IsValid)
        {
            Debug.LogWarning("MusicManager: MusicCue is missing or empty.");
            return;
        }

        if (currentCue == cue && cueRoutine != null && !restartIfSameCue)
        {
            return;
        }

        StopCueRoutine();

        currentCue = cue;
        cueState = new MusicSelectionState();
        cueRoutine = StartCoroutine(PlayCueRoutine(cue));
    }

    public void PlayTrack(MusicTrack track, bool restartIfSameTrack = false)
    {
        StopCueRoutine();
        currentCue = null;

        PlayTrackInternal(track, restartIfSameTrack);
    }

    public void StopMusic(float fadeOutTime = 1f)
    {
        StopCueRoutine();
        currentCue = null;

        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        if (!isActiveAndEnabled || fadeOutTime <= 0f)
        {
            StopMusicImmediate();
            return;
        }

        transitionCoroutine = StartCoroutine(FadeOutAndStop(fadeOutTime));
    }

    public void StopCue(MusicCue cue, float fadeOutTime = 1f)
    {
        if (cue == null || currentCue != cue)
        {
            return;
        }

        StopMusic(fadeOutTime);
    }

    public void Pause()
    {
        if (currentSource != null)
        {
            currentSource.Pause();
        }

        if (nextSource != null)
        {
            nextSource.Pause();
        }
    }

    public void Resume()
    {
        if (currentSource != null)
        {
            currentSource.UnPause();
        }

        if (nextSource != null)
        {
            nextSource.UnPause();
        }
    }

    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);

        if (currentTrack != null && currentSource != null)
        {
            currentSource.volume = currentTrack.Volume * masterVolume;
        }
    }

    private IEnumerator PlayCueRoutine(MusicCue cue)
    {
        while (true)
        {
            MusicTrack track = GetNextTrack(cue);

            if (track == null || track.Clip == null)
            {
                Debug.LogWarning("MusicManager: Track or AudioClip is missing.");
                cueRoutine = null;
                yield break;
            }

            bool shouldRestartIfSameTrack = cueState.PlayedCount > 0;
            PlayTrackInternal(track, shouldRestartIfSameTrack);

            cueState.LastTrack = track;
            cueState.PlayedCount++;

            if (!ShouldScheduleNextTrack(cue))
            {
                cueRoutine = null;
                yield break;
            }

            float waitTime = Mathf.Max(
                0f,
                track.Clip.length - cue.CrossfadeBeforeTrackEnds
            );

            yield return Wait(waitTime);

            if (cue.DelayBetweenTracks > 0f)
            {
                yield return Wait(cue.DelayBetweenTracks);
            }
        }
    }

    private MusicTrack GetNextTrack(MusicCue cue)
    {
        if (cue.Selector == null)
        {
            return cue.GetFirstTrack();
        }

        return cue.Selector.SelectNext(cue.Tracks, cueState);
    }

    private bool ShouldScheduleNextTrack(MusicCue cue)
    {
        if (cue.LoopCue)
        {
            return true;
        }

        if (!cue.ContinueAfterTrackEnds || cue.Selector == null)
        {
            return false;
        }

        return cueState.PlayedCount < cue.Tracks.Length;
    }

    private void PlayTrackInternal(MusicTrack track, bool restartIfSameTrack)
    {
        if (track == null || track.Clip == null)
        {
            Debug.LogWarning("MusicManager: Track or AudioClip is missing.");
            return;
        }

        if (currentTrack == track && currentSource.isPlaying && !restartIfSameTrack)
        {
            return;
        }

        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
        }

        transitionCoroutine = StartCoroutine(CrossfadeToTrack(track));
    }

    private void StopCueRoutine()
    {
        if (cueRoutine == null) return;

        StopCoroutine(cueRoutine);
        cueRoutine = null;
        cueState = null;
    }

    private AudioSource CreateAudioSource(string sourceName)
    {
        GameObject sourceObject = new GameObject(sourceName);
        sourceObject.transform.SetParent(transform);

        AudioSource source = sourceObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.outputAudioMixerGroup = musicMixerGroup;

        return source;
    }

    private IEnumerator CrossfadeToTrack(MusicTrack newTrack)
    {
        AudioSource outgoingSource = currentSource;
        AudioSource incomingSource = nextSource;

        incomingSource.Stop();
        incomingSource.clip = newTrack.Clip;
        incomingSource.loop = false;
        incomingSource.volume = 0f;
        incomingSource.Play();

        float fadeInTime = newTrack.FadeInTime;
        float fadeOutTime = currentTrack != null ? currentTrack.FadeOutTime : newTrack.FadeInTime;

        float duration = Mathf.Max(fadeInTime, fadeOutTime);

        float outgoingStartVolume = outgoingSource.volume;
        float incomingTargetVolume = newTrack.Volume * masterVolume;

        float timer = 0f;

        while (timer < duration)
        {
            timer += GetDeltaTime();

            if (fadeOutTime > 0f)
            {
                float outT = Mathf.Clamp01(timer / fadeOutTime);
                outgoingSource.volume = Mathf.Lerp(outgoingStartVolume, 0f, outT);
            }
            else
            {
                outgoingSource.volume = 0f;
            }

            if (fadeInTime > 0f)
            {
                float inT = Mathf.Clamp01(timer / fadeInTime);
                incomingSource.volume = Mathf.Lerp(0f, incomingTargetVolume, inT);
            }
            else
            {
                incomingSource.volume = incomingTargetVolume;
            }

            yield return null;
        }

        outgoingSource.Stop();
        outgoingSource.clip = null;
        outgoingSource.volume = 0f;

        incomingSource.volume = incomingTargetVolume;

        currentSource = incomingSource;
        nextSource = outgoingSource;
        currentTrack = newTrack;

        transitionCoroutine = null;
    }

    private IEnumerator FadeOutAndStop(float fadeOutTime)
    {
        float currentStartVolume = currentSource != null ? currentSource.volume : 0f;
        float nextStartVolume = nextSource != null ? nextSource.volume : 0f;

        float timer = 0f;

        while (timer < fadeOutTime)
        {
            timer += GetDeltaTime();

            float t = fadeOutTime > 0f ? Mathf.Clamp01(timer / fadeOutTime) : 1f;

            if (currentSource != null)
            {
                currentSource.volume = Mathf.Lerp(currentStartVolume, 0f, t);
            }

            if (nextSource != null)
            {
                nextSource.volume = Mathf.Lerp(nextStartVolume, 0f, t);
            }

            yield return null;
        }

        if (currentSource != null)
        {
            currentSource.Stop();
            currentSource.clip = null;
            currentSource.volume = 0f;
        }

        if (nextSource != null)
        {
            nextSource.Stop();
            nextSource.clip = null;
            nextSource.volume = 0f;
        }

        currentTrack = null;
        currentCue = null;
        cueState = null;
        transitionCoroutine = null;
    }

    private void StopMusicImmediate()
    {
        if (currentSource != null)
        {
            currentSource.Stop();
            currentSource.clip = null;
            currentSource.volume = 0f;
        }

        if (nextSource != null)
        {
            nextSource.Stop();
            nextSource.clip = null;
            nextSource.volume = 0f;
        }

        currentTrack = null;
        currentCue = null;
        cueState = null;
        transitionCoroutine = null;
    }

    private IEnumerator Wait(float seconds)
    {
        if (useUnscaledTime)
        {
            yield return new WaitForSecondsRealtime(seconds);
        }
        else
        {
            yield return new WaitForSeconds(seconds);
        }
    }

    private float GetDeltaTime()
    {
        return useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
    }
}
