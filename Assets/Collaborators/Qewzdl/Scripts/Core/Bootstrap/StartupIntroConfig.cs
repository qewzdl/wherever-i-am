using UnityEngine;
using UnityEngine.Video;

// Everything about the film that opens the game, in one asset.
//
// It began as fields on the component in Bootstrap, which meant that changing
// the volume of a title card was a reason to open a scene - and the scene it
// was a reason to open is the one every other part of the game is wired
// through. Configs in this project are assets for exactly that reason: the
// lobby's, the connection's and the crosshair's all sit next to the thing they
// describe rather than inside the scene that happens to use them.
[CreateAssetMenu(
    fileName = "StartupIntroConfig",
    menuName = "Wherever I Am/Startup Intro Config")]
public sealed class StartupIntroConfig : ScriptableObject
{
    [Header("Film")]
    [SerializeField] private VideoClip clip;

    [Tooltip("The clip's own audio track. Its own number rather than the " +
             "master volume: on a first launch nobody has been able to reach " +
             "the settings screen yet.")]
    [SerializeField, Range(0f, 1f)] private float volume = 1f;

    [Header("While working")]
    [Tooltip("Off while working on a scene directly, so the film is not " +
             "watched forty times an afternoon. It never plays when the " +
             "editor was asked to start somewhere other than the menu.")]
    [SerializeField] private bool playInEditor = true;

    [Header("Giving up")]
    [Tooltip("How long to wait for the clip to open before carrying on " +
             "without it. A missing codec must not become a black screen " +
             "with no way out of it.")]
    [SerializeField, Min(1f)] private float prepareTimeoutSeconds = 5f;

    [Tooltip("Added to the clip's own length before giving up on it ending. " +
             "Covers a slow first frame, not a broken file.")]
    [SerializeField, Min(1f)] private float playbackSlackSeconds = 3f;

    [Header("Leaving")]
    [Tooltip("Matches --motion-screen in the theme, which is what the class " +
             "on the panel actually animates.")]
    [SerializeField, Min(0f)] private float fadeSeconds = 0.26f;

    public VideoClip Clip => clip;
    public float Volume => Mathf.Clamp01(volume);
    public bool PlayInEditor => playInEditor;
    public float PrepareTimeoutSeconds => Mathf.Max(1f, prepareTimeoutSeconds);
    public float PlaybackSlackSeconds => Mathf.Max(1f, playbackSlackSeconds);
    public float FadeSeconds => Mathf.Max(0f, fadeSeconds);

    // The clip knows its own size without anybody opening it, and a clip with
    // no picture in it is the one thing here worth refusing outright: the
    // intro has no other way off the screen, so it must not start at all
    // rather than start and have nothing to show.
    public bool HasPicture => clip != null && clip.width > 0 && clip.height > 0;
}
