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
        if (activeTheme == null)
        {
            return;
        }

        if (!activeTheme.TryGetSound(type, out SoundEffect sound))
        {
            return;
        }

        Play(sound);
    }

    public void Play(SoundEffect sound)
    {
        if (sound == null)
        {
            return;
        }

        if (source == null)
        {
            Debug.LogWarning("UiSoundManager: AudioSource is missing.");
            return;
        }

        AudioClip clip = sound.GetClip();

        if (clip == null)
        {
            Debug.LogWarning("UiSoundManager: SoundEffect has no AudioClip.");
            return;
        }

        source.pitch = sound.GetPitch();
        source.PlayOneShot(clip, sound.GetVolume() * masterVolume);
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