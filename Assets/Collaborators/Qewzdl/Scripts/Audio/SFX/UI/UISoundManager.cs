using UnityEngine;
using UnityEngine.Audio;

public class UISoundManager : MonoBehaviour
{
    [Header("Mixer")]
    [SerializeField] private AudioMixerGroup uiMixerGroup;

    [Header("Settings")]
    [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;

    [Header("Default UI Sounds")]
    [SerializeField] private SoundEffect clickSound;
    [SerializeField] private SoundEffect hoverSound;
    [SerializeField] private SoundEffect openSound;
    [SerializeField] private SoundEffect closeSound;
    [SerializeField] private SoundEffect confirmSound;
    [SerializeField] private SoundEffect cancelSound;
    [SerializeField] private SoundEffect errorSound;
    [SerializeField] private SoundEffect inputSound;

    private AudioSource source;

    private void Awake()
    {
        source = CreateAudioSource();
    }

    public void PlayClick()
    {
        Play(clickSound);
    }

    public void PlayHover()
    {
        Play(hoverSound);
    }

    public void PlayOpen()
    {
        Play(openSound);
    }

    public void PlayClose()
    {
        Play(closeSound);
    }

    public void PlayConfirm()
    {
        Play(confirmSound);
    }

    public void PlayCancel()
    {
        Play(cancelSound);
    }

    public void PlayError()
    {
        Play(errorSound);
    }

    public void PlayInput()
    {
        Play(inputSound);
    }

    public void Play(SoundEffect sound)
    {
        if (sound == null)
        {
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