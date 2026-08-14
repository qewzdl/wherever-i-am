using UnityEngine;

public class AudioManager : MonoBehaviour, IAudioService
{
    [Header("Latency")]
    [SerializeField, Min(64)] private int targetDspBufferSize = 256;

    [Header("Audio Managers")]
    [SerializeField] private MusicManager music;
    [SerializeField] private UiSoundManager ui;
    [SerializeField] private GameplaySoundManager gameplay;

    private SettingsServiceComposition settingsComposition;

    public MusicManager Music => music;
    public UiSoundManager UI => ui;
    public GameplaySoundManager Gameplay => gameplay;

    IMusicService IAudioService.Music => music;
    IUiSoundService IAudioService.UI => ui;
    IGameplaySoundService IAudioService.Gameplay => gameplay;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);

        ApplyLowLatencyConfiguration();
        FindMissingManagers();
        ValidateManagers();
    }

    public bool Construct(
        IProjectSceneRegistry sceneRegistry,
        ISettingsService settingsService)
    {
        FindMissingManagers();
        settingsComposition?.Dispose();
        settingsComposition = null;

        if (!SettingsServiceComposition.TryCompose(
                gameObject,
                settingsService,
                out settingsComposition))
        {
            Debug.LogError($"{nameof(AudioManager)} failed to compose settings consumers.", this);
            return false;
        }

        SceneAudioDirector director = GetComponentInChildren<SceneAudioDirector>(true);

        if (director == null)
        {
            Debug.LogError(
                $"{nameof(AudioManager)} requires a child {nameof(SceneAudioDirector)}.",
                this);

            settingsComposition.Dispose();
            settingsComposition = null;
            return false;
        }

        director.Construct(this, sceneRegistry);
        bool valid = music != null && ui != null && gameplay != null;

        if (!valid)
        {
            settingsComposition.Dispose();
            settingsComposition = null;
        }

        return valid;
    }

    private void OnDestroy()
    {
        settingsComposition?.Dispose();
        settingsComposition = null;
    }

    private void ApplyLowLatencyConfiguration()
    {
        AudioConfiguration configuration = AudioSettings.GetConfiguration();
        int requestedBufferSize = Mathf.Max(64, targetDspBufferSize);

        if (configuration.dspBufferSize != requestedBufferSize)
        {
            configuration.dspBufferSize = requestedBufferSize;

            if (!AudioSettings.Reset(configuration))
            {
                Debug.LogWarning(
                    $"{nameof(AudioManager)} could not apply DSP buffer size " +
                    $"{requestedBufferSize}.",
                    this
                );
            }
        }

        AudioConfiguration appliedConfiguration =
            AudioSettings.GetConfiguration();

        RuntimeLog.Info(
            $"Audio configuration: sampleRate=" +
            $"{appliedConfiguration.sampleRate}, dspBufferSize=" +
            $"{appliedConfiguration.dspBufferSize}, speakerMode=" +
            $"{appliedConfiguration.speakerMode}."
        );
    }

    private void FindMissingManagers()
    {
        if (music == null)
        {
            music = GetComponentInChildren<MusicManager>();
        }

        if (ui == null)
        {
            ui = GetComponentInChildren<UiSoundManager>();
        }

        if (gameplay == null)
        {
            gameplay = GetComponentInChildren<GameplaySoundManager>();
        }       
    }

    private void ValidateManagers()
    {
        if (music == null)
        {
            Debug.LogWarning("MusicManager is missing.");
        }

        if (ui == null)
        {
            Debug.LogWarning("UiSoundManager is missing.");
        }

        if (gameplay == null)
        {
            Debug.LogWarning("GameplaySoundManager is missing.");
        }    
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        targetDspBufferSize = Mathf.Max(64, targetDspBufferSize);
    }
#endif
}
