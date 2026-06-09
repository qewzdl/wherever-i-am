using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Latency")]
    [SerializeField, Min(64)] private int targetDspBufferSize = 256;

    [Header("Audio Managers")]
    [SerializeField] private MusicManager music;
    [SerializeField] private UiSoundManager ui;
    [SerializeField] private GameplaySoundManager gameplay;

    public MusicManager Music => music;
    public UiSoundManager UI => ui;
    public GameplaySoundManager Gameplay => gameplay;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        ApplyLowLatencyConfiguration();
        FindMissingManagers();
        ValidateManagers();
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
