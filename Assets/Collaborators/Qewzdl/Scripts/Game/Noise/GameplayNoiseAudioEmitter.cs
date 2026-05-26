using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class GameplayNoiseAudioEmitter : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField, Range(0f, 1f)] private float defaultVolumeScale = 1f;

    [Header("Gameplay Noise")]
    [SerializeField] private GameplayNoiseEmitter noiseEmitter;
    [SerializeField] private bool emitNoise = true;

    private bool invalidConfigurationLogged;
    private bool missingClipLogged;

    public bool IsConfigured => ValidateStaticDependencies(false);

    private void Awake()
    {
        CacheComponents();

        if (!ValidateStaticDependencies())
        {
            enabled = false;
        }
    }

    public bool PlayOneShotAndEmit(AudioClip clip)
    {
        return PlayOneShotAndEmit(clip, defaultVolumeScale);
    }

    public bool PlayOneShotAndEmit(AudioClip clip, float volumeScale)
    {
        if (!ValidateStaticDependencies())
        {
            return false;
        }

        bool played = TryPlayOneShot(clip, volumeScale);
        bool emitted = !emitNoise || TryEmitNoise();

        return played && emitted;
    }

    public bool TryEmitNoise()
    {
        if (!ValidateStaticDependencies())
        {
            return false;
        }

        if (noiseEmitter.IsServer)
        {
            return noiseEmitter.TryEmitServer();
        }

        noiseEmitter.RequestEmitFromOwner();
        return true;
    }

    private bool TryPlayOneShot(AudioClip clip, float volumeScale)
    {
        if (clip == null)
        {
            LogMissingClip();
            return false;
        }

        missingClipLogged = false;
        audioSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
        return true;
    }

    private void CacheComponents()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (noiseEmitter == null)
        {
            noiseEmitter = GetComponent<GameplayNoiseEmitter>();
        }
    }

    private bool ValidateStaticDependencies()
    {
        return ValidateStaticDependencies(true);
    }

    private bool ValidateStaticDependencies(bool logErrors)
    {
        CacheComponents();

        StringBuilder builder = new();

        if (audioSource == null)
        {
            EnemyValidationLogger.AppendMissingDependency(
                builder,
                nameof(audioSource)
            );
        }

        if (emitNoise && noiseEmitter == null)
        {
            EnemyValidationLogger.AppendMissingDependency(
                builder,
                nameof(noiseEmitter)
            );
        }

        return EnemyValidationLogger.ValidateAndLog(
            this,
            nameof(GameplayNoiseAudioEmitter),
            builder,
            ref invalidConfigurationLogged,
            logErrors,
            "Gameplay noise audio emitter is disabled until configured."
        );
    }

    private void LogMissingClip()
    {
        if (missingClipLogged)
        {
            return;
        }

        missingClipLogged = true;

        Debug.LogError(
            $"{nameof(GameplayNoiseAudioEmitter)} requires non-null {nameof(AudioClip)}.",
            this
        );
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        defaultVolumeScale = Mathf.Clamp01(defaultVolumeScale);
        ValidateStaticDependencies();
    }
#endif
}