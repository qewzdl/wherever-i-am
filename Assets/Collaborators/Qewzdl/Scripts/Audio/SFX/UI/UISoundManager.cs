using UnityEngine;
using UnityEngine.Audio;

public class UiSoundManager : MonoBehaviour
{
    [Header("Mixer")]
    [SerializeField] private AudioMixerGroup uiMixerGroup;

    [Header("Settings")]
    [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;

    private AudioSource source;
    private UiSoundTheme activeTheme;

    private void Awake()
    {
        source = CreateAudioSource();
    }

    public void ApplyTheme(UiSoundTheme theme)
    {
        activeTheme = theme;
    }

    public void ClearTheme()
    {
        activeTheme = null;
    }

    public void PlayClick()
    {
        Play(UiSoundType.Click);
    }

    public void PlayHover()
    {
        Play(UiSoundType.Hover);
    }

    public void PlayOpen()
    {
        Play(UiSoundType.Open);
    }

    public void PlayClose()
    {
        Play(UiSoundType.Close);
    }

    public void PlayConfirm()
    {
        Play(UiSoundType.Confirm);
    }

    public void PlayCancel()
    {
        Play(UiSoundType.Cancel);
    }

    public void PlayError()
    {
        Play(UiSoundType.Error);
    }

    public void PlayInput()
    {
        Play(UiSoundType.Input);
    }

    public void Play(UiSoundType type)
    {
        TryPlay(type);
    }

    public bool TryPlay(UiSoundType type)
    {
        if (activeTheme == null)
        {
            return false;
        }

        if (!activeTheme.TryGetSound(type, out SoundEffect sound))
        {
            return false;
        }

        return TryPlay(sound);
    }

    public void Play(SoundEffect sound)
    {
        TryPlay(sound);
    }

    public bool TryPlay(SoundEffect sound)
    {
        if (sound == null)
        {
            return false;
        }

        if (source == null)
        {
            Debug.LogWarning("UiSoundManager: AudioSource is missing.");
            return false;
        }

        AudioClip clip = sound.GetClip();

        if (clip == null)
        {
            Debug.LogWarning("UiSoundManager: SoundEffect has no AudioClip.");
            return false;
        }

        source.pitch = sound.GetPitch();
        source.PlayOneShot(clip, sound.GetVolume() * masterVolume);
        return true;
    }

    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);
    }

    private AudioSource CreateAudioSource()
    {
        GameObject sourceObject = new GameObject("UI Sound Source");
        sourceObject.transform.SetParent(transform);

        AudioSource audioSource = sourceObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSource.outputAudioMixerGroup = uiMixerGroup;

        return audioSource;
    }
}
