using UnityEngine;

[CreateAssetMenu(fileName = "SceneAudioProfile", menuName = "Game Audio/Scene Audio Profile")]
public class SceneAudioProfile : ScriptableObject
{
    [Header("Scenes")]
    [SerializeField] private string[] sceneNames;

    [Header("Music")]
    [SerializeField] private MusicCue musicCue;
    [SerializeField] private bool restartMusicIfSameCue = false;
    [SerializeField] private bool stopMusicIfNoCue = true;
    [SerializeField, Min(0f)] private float musicFadeOutTime = 1f;

    [Header("UI Sounds")]
    [SerializeField] private UiSoundTheme uiSoundTheme;
    [SerializeField] private bool clearUiThemeIfMissing = true;

    public MusicCue MusicCue => musicCue;
    public bool RestartMusicIfSameCue => restartMusicIfSameCue;
    public bool StopMusicIfNoCue => stopMusicIfNoCue;
    public float MusicFadeOutTime => musicFadeOutTime;

    public UiSoundTheme UiSoundTheme => uiSoundTheme;
    public bool ClearUiThemeIfMissing => clearUiThemeIfMissing;

    public bool MatchesScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) return false;
        if (sceneNames == null || sceneNames.Length == 0) return false;

        for (int i = 0; i < sceneNames.Length; i++)
        {
            if (sceneNames[i] == sceneName)
            {
                return true;
            }
        }

        return false;
    }
}