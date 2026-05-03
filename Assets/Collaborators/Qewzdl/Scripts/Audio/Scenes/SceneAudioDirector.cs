using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneAudioDirector : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private AudioManager audioManager;
    [SerializeField] private SceneAudioRegistry registry;

    [Header("Settings")]
    [SerializeField] private bool applyOnStart = true;
    [SerializeField] private bool applyForAdditiveScenes = false;
    [SerializeField] private bool logMissingProfile = true;

    private string currentSceneName;

    private void Awake()
    {
        if (audioManager == null)
        {
            audioManager = GetComponentInParent<AudioManager>();
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void Start()
    {
        if (!applyOnStart) return;

        Scene activeScene = SceneManager.GetActiveScene();
        ApplySceneProfile(activeScene.name);
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode loadMode)
    {
        if (loadMode == LoadSceneMode.Additive && !applyForAdditiveScenes)
        {
            return;
        }

        ApplySceneProfile(scene.name);
    }

    private void ApplySceneProfile(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) return;

        if (currentSceneName == sceneName)
        {
            return;
        }

        currentSceneName = sceneName;

        if (audioManager == null)
        {
            Debug.LogWarning("SceneAudioDirector: AudioManager is missing.");
            return;
        }

        if (registry == null)
        {
            Debug.LogWarning("SceneAudioDirector: SceneAudioRegistry is missing.");
            return;
        }

        SceneAudioProfile profile = registry.GetProfileForScene(sceneName);

        if (profile == null)
        {
            if (logMissingProfile)
            {
                Debug.LogWarning($"SceneAudioDirector: No audio profile found for scene '{sceneName}'.");
            }

            return;
        }

        ApplyMusic(profile);
        ApplyUiSounds(profile);
    }

    private void ApplyMusic(SceneAudioProfile profile)
    {
        if (audioManager.Music == null)
        {
            Debug.LogWarning("SceneAudioDirector: MusicManager is missing.");
            return;
        }

        if (profile.MusicCue != null)
        {
            audioManager.Music.PlayCue(profile.MusicCue, profile.RestartMusicIfSameCue);
            return;
        }

        if (profile.StopMusicIfNoCue)
        {
            audioManager.Music.StopMusic(profile.MusicFadeOutTime);
        }
    }

    private void ApplyUiSounds(SceneAudioProfile profile)
    {
        if (audioManager.UI == null)
        {
            Debug.LogWarning("SceneAudioDirector: UiSoundManager is missing.");
            return;
        }

        if (profile.UiSoundTheme != null)
        {
            audioManager.UI.ApplyTheme(profile.UiSoundTheme);
            return;
        }

        if (profile.ClearUiThemeIfMissing)
        {
            audioManager.UI.ClearTheme();
        }
    }
}