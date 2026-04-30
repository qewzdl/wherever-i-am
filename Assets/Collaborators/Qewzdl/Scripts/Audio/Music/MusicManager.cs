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
    private Coroutine transitionCoroutine;

    public MusicTrack CurrentTrack => currentTrack;
    public bool IsPlaying => currentSource != null && currentSource.isPlaying;

    private void Awake()
    {
        currentSource = CreateAudioSource("Current Music Source");
        nextSource = CreateAudioSource("Next Music Source");
    }

    public void PlayTrack(MusicTrack track, bool restartIfSameTrack = false)
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

    public void StopMusic(float fadeOutTime = 1f)
    {
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
        }

        transitionCoroutine = StartCoroutine(FadeOutAndStop(fadeOutTime));
    }

    public void Pause()
    {
        currentSource.Pause();
        nextSource.Pause();
    }

    public void Resume()
    {
        currentSource.UnPause();
        nextSource.UnPause();
    }

    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);

        if (currentTrack != null)
        {
            currentSource.volume = currentTrack.Volume * masterVolume;
        }
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
        float currentStartVolume = currentSource.volume;
        float nextStartVolume = nextSource.volume;

        float timer = 0f;

        while (timer < fadeOutTime)
        {
            timer += GetDeltaTime();

            float t = fadeOutTime > 0f ? Mathf.Clamp01(timer / fadeOutTime) : 1f;

            currentSource.volume = Mathf.Lerp(currentStartVolume, 0f, t);
            nextSource.volume = Mathf.Lerp(nextStartVolume, 0f, t);

            yield return null;
        }

        currentSource.Stop();
        nextSource.Stop();

        currentSource.clip = null;
        nextSource.clip = null;

        currentSource.volume = 0f;
        nextSource.volume = 0f;

        currentTrack = null;
        transitionCoroutine = null;
    }

    private float GetDeltaTime()
    {
        return useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
    }
}
