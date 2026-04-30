using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Audio Managers")]
    [SerializeField] private MusicManager music;
    [SerializeField] private UISoundManager ui;
    [SerializeField] private GameplaySoundManager gameplay;

    public MusicManager Music => music;
    public UISoundManager UI => ui;
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

        FindMissingManagers();
        ValidateManagers();
    }

    private void FindMissingManagers()
    {
        if (music == null)
        {
            music = GetComponentInChildren<MusicManager>();
        }

        if (ui == null)
        {
            ui = GetComponentInChildren<UISoundManager>();
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
}
