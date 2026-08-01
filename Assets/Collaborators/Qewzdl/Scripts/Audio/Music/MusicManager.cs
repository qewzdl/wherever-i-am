using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

public class MusicManager : MonoBehaviour, IMusicService
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
    private ISettingsService settingsService;
    private float currentSourceBaseVolume;
    private float nextSourceBaseVolume;

    public MusicTrack CurrentTrack => currentTrack;
    public MusicCue CurrentCue => currentCue;
    public bool IsPlaying => currentSource != null && currentSource.isPlaying;

    private void Awake()
    {
        currentSource = CreateAudioSource("Current Music Source");
        nextSource = CreateAudioSource("Next Music Source");
    }

    private void OnDestroy()
    {
        UnbindSettings();
    }

    public void BindSettings()
    {
        UnbindSettings();

        if (!SettingsService.TryGet(out ISettingsService service))
            return;

        settingsService = service;
        settingsService.MusicGainChanged += HandleMusicGainChanged;
        SetMasterVolume(settingsService.Current.masterVolume * settingsService.Current.musicVolume);
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

        if (!CanStartCoroutines() || fadeOutTime <= 0f)
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
        RefreshSourceVolumes();
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

        // Only skip the work when nothing is in flight. A fade-out started by
        // StopMusic is still a transition: bailing out here would leave it
        // running and it would silence the track we were just asked to play.
        if (currentTrack == track &&
            currentSource.isPlaying &&
            !restartIfSameTrack &&
            transitionCoroutine == null)
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
        nextSourceBaseVolume = 0f;
        RefreshSourceVolumes();
        incomingSource.Play();

        float fadeInTime = newTrack.FadeInTime;
        float fadeOutTime = currentTrack != null ? currentTrack.FadeOutTime : newTrack.FadeInTime;

        float duration = Mathf.Max(fadeInTime, fadeOutTime);

        float outgoingStartBaseVolume = currentSourceBaseVolume;
        float incomingTargetBaseVolume = newTrack.Volume;

        float timer = 0f;

        while (timer < duration)
        {
            timer += GetDeltaTime();

            if (fadeOutTime > 0f)
            {
                float outT = Mathf.Clamp01(timer / fadeOutTime);
                currentSourceBaseVolume = Mathf.Lerp(outgoingStartBaseVolume, 0f, outT);
            }
            else
            {
                currentSourceBaseVolume = 0f;
            }

            if (fadeInTime > 0f)
            {
                float inT = Mathf.Clamp01(timer / fadeInTime);
                nextSourceBaseVolume = Mathf.Lerp(0f, incomingTargetBaseVolume, inT);
            }
            else
            {
                nextSourceBaseVolume = incomingTargetBaseVolume;
            }

            RefreshSourceVolumes();

            yield return null;
        }

        outgoingSource.Stop();
        outgoingSource.clip = null;
        currentSource = incomingSource;
        nextSource = outgoingSource;
        currentSourceBaseVolume = incomingTargetBaseVolume;
        nextSourceBaseVolume = 0f;
        RefreshSourceVolumes();
        currentTrack = newTrack;

        transitionCoroutine = null;
    }

    private IEnumerator FadeOutAndStop(float fadeOutTime)
    {
        float currentStartBaseVolume = currentSourceBaseVolume;
        float nextStartBaseVolume = nextSourceBaseVolume;

        float timer = 0f;

        while (timer < fadeOutTime)
        {
            timer += GetDeltaTime();

            float t = fadeOutTime > 0f ? Mathf.Clamp01(timer / fadeOutTime) : 1f;

            if (currentSource != null)
            {
                currentSourceBaseVolume = Mathf.Lerp(currentStartBaseVolume, 0f, t);
            }

            if (nextSource != null)
            {
                nextSourceBaseVolume = Mathf.Lerp(nextStartBaseVolume, 0f, t);
            }

            RefreshSourceVolumes();

            yield return null;
        }

        if (currentSource != null)
        {
            currentSource.Stop();
            currentSource.clip = null;
            currentSourceBaseVolume = 0f;
        }

        if (nextSource != null)
        {
            nextSource.Stop();
            nextSource.clip = null;
            nextSourceBaseVolume = 0f;
        }

        // Deliberately does not touch currentCue or cueState. StopMusic clears
        // both through StopCueRoutine before starting this fade, so repeating
        // it here is redundant on the normal path - and actively harmful on the
        // late one, where a newer cue is already running and would have its
        // state pulled out from under a live PlayCueRoutine.
        currentTrack = null;
        transitionCoroutine = null;
    }

    private void StopMusicImmediate()
    {
        if (currentSource != null)
        {
            currentSource.Stop();
            currentSource.clip = null;
            currentSourceBaseVolume = 0f;
        }

        if (nextSource != null)
        {
            nextSource.Stop();
            nextSource.clip = null;
            nextSourceBaseVolume = 0f;
        }

        // Same reasoning as FadeOutAndStop: cue ownership belongs to StopMusic.
        currentTrack = null;
        transitionCoroutine = null;
    }

    private bool CanStartCoroutines()
    {
        return enabled && gameObject.activeInHierarchy;
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

    private void HandleMusicGainChanged(float gain)
    {
        SetMasterVolume(gain);
    }

    private void UnbindSettings()
    {
        if (settingsService != null)
            settingsService.MusicGainChanged -= HandleMusicGainChanged;

        settingsService = null;
    }

    private void RefreshSourceVolumes()
    {
        if (currentSource != null)
            currentSource.volume = currentSourceBaseVolume * masterVolume;

        if (nextSource != null)
            nextSource.volume = nextSourceBaseVolume * masterVolume;
    }
}
