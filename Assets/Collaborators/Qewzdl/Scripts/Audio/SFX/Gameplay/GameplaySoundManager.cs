using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class GameplaySoundManager : MonoBehaviour, IGameplaySoundService
{
    [Header("Mixer")]
    [SerializeField] private AudioMixerGroup gameplayMixerGroup;

    [Header("Pool")]
    [SerializeField, Min(1)] private int initialPoolSize = 20;
    [SerializeField] private bool expandPoolIfNeeded = true;

    [Header("Settings")]
    [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;

    private readonly List<AudioSource> sources = new List<AudioSource>();
    private Transform poolRoot;

    private void Awake()
    {
        CreatePoolRoot();
        CreateInitialPool();
    }

    public void Play2D(SoundEffect sound)
    {
        Play(sound, transform.position, 0f);
    }

    public void PlayAtPosition(SoundEffect sound, Vector3 position)
    {
        if (sound == null)
        {
            return;
        }

        Play(sound, position, sound.SpatialBlend);
    }

    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);
    }

    private void Play(SoundEffect sound, Vector3 position, float spatialBlend)
    {
        if (sound == null)
        {
            return;
        }

        AudioClip clip = sound.GetClip();

        if (clip == null)
        {
            Debug.LogWarning("GameplaySoundManager: SoundEffect has no AudioClip.");
            return;
        }

        AudioSource source = GetAvailableSource();

        if (source == null)
        {
            Debug.LogWarning("GameplaySoundManager: No available AudioSource in pool.");
            return;
        }

        source.Stop();

        source.transform.SetParent(poolRoot);
        source.transform.position = position;

        source.clip = clip;
        source.volume = sound.GetVolume() * GetEffectiveVolume();
        source.pitch = sound.GetPitch();
        source.spatialBlend = spatialBlend;
        source.minDistance = sound.MinDistance;
        source.maxDistance = sound.MaxDistance;

        source.Play();
    }

    private void CreatePoolRoot()
    {
        GameObject rootObject = new GameObject("Gameplay Sound Pool");
        rootObject.transform.SetParent(transform);

        poolRoot = rootObject.transform;
    }

    private void CreateInitialPool()
    {
        for (int i = 0; i < initialPoolSize; i++)
        {
            CreateSource();
        }
    }

    private AudioSource CreateSource()
    {
        GameObject sourceObject = new GameObject("Gameplay Sound Source");
        sourceObject.transform.SetParent(poolRoot);

        AudioSource source = sourceObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;
        source.outputAudioMixerGroup = gameplayMixerGroup;

        sources.Add(source);

        return source;
    }

    private AudioSource GetAvailableSource()
    {
        for (int i = 0; i < sources.Count; i++)
        {
            if (!sources[i].isPlaying)
            {
                return sources[i];
            }
        }

        if (expandPoolIfNeeded)
        {
            return CreateSource();
        }

        return null;
    }

    private float GetEffectiveVolume()
    {
        if (SettingsService.TryGet(out ISettingsService settings))
        {
            GameSettingsData current = settings.Current;
            return Mathf.Clamp01(current.masterVolume) * Mathf.Clamp01(current.effectsVolume);
        }

        return masterVolume;
    }
}
