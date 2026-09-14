using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

// The name of the game as a film instead of a word.
//
// UI Toolkit cannot play video. What it can do is show a RenderTexture as an
// element's background, so a VideoPlayer writes frames into one and the
// masthead label wears it - the same route the startup film already takes, and
// the reason that one carries a comment about settling the output before
// Prepare rather than after.
//
// The label keeps its text until the first frame is ready, and gets it back if
// the clip will not open. A masthead that says nothing at all is worse than one
// that says the name, and this is the one place in the menu where an empty
// element would read as the game having failed to start rather than as a
// missing video.
[DisallowMultipleComponent]
public sealed class MainMenuTitleVideo : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument document;

    [Header("Film")]
    [SerializeField] private VideoClip clip;

    // The height the name occupies on the screen. The width follows from the
    // clip's own shape, so a film of any proportion lands at the right one
    // without anybody measuring it - and swapping the clip for a wider cut
    // needs nothing changed here.
    [SerializeField, Min(1f)] private float displayHeight = 140f;

    private VideoPlayer player;
    private RenderTexture frames;
    private VisualElement boundRoot;
    private Label title;
    private string fallbackText = string.Empty;
    private bool isShowingFilm;

    private void Awake()
    {
        if (document == null)
            document = GetComponent<UIDocument>();

        // Added here rather than placed in the scene, for the reason the
        // startup film gives: every setting that matters is written below, so a
        // serialised copy would only be somewhere for those to differ quietly.
        player = gameObject.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
    }

    // Twice, because UIDocument builds its tree in its own OnEnable and
    // component order on one object is not something to rely on. Bind returns
    // early on a root it has already seen, so the second call costs nothing.
    private void OnEnable()
    {
        Bind();
    }

    private void Start()
    {
        Bind();
    }

    // The name is a localised string, so the table writes it back into the
    // label whenever the language changes - straight over the film, because
    // nothing in the localisation knows this label has stopped being text.
    private void HandleLanguageChanged()
    {
        if (isShowingFilm)
            ShowFilm();
    }

    private void OnDestroy()
    {
        UiLocalization.Changed -= HandleLanguageChanged;

        if (player != null)
        {
            player.prepareCompleted -= HandlePrepared;
            player.errorReceived -= HandleError;
        }

        ReleaseFrames();
    }

    private void Bind()
    {
        VisualElement root = document != null ? document.rootVisualElement : null;

        if (root == null)
            return;

        // A document that is switched off rebuilds its tree, which leaves the
        // old label behind and the new one wearing nothing. Rebinding puts the
        // picture back on whichever label is currently on screen.
        if (ReferenceEquals(root, boundRoot) && title != null)
            return;

        boundRoot = root;
        title = root.Q<Label>("Title");

        UiLocalization.Changed -= HandleLanguageChanged;
        UiLocalization.Changed += HandleLanguageChanged;

        if (title == null || clip == null)
            return;

        if (isShowingFilm)
        {
            ShowFilm();
            return;
        }

        fallbackText = title.text;

        if (!TryMakeFrames())
            return;

        StartClip();
    }

    // Somewhere for the frames to be, at the clip's own size. A RenderTexture
    // is the only kind of live texture an element's background can be made
    // from.
    private bool TryMakeFrames()
    {
        if (frames != null)
            return true;

        int width = (int)clip.width;
        int height = (int)clip.height;

        if (width <= 0 || height <= 0)
        {
            Debug.LogWarning(
                $"{nameof(MainMenuTitleVideo)} was given a clip with no " +
                $"picture in it ({width}x{height}).",
                this);

            return false;
        }

        frames = new RenderTexture(width, height, 0)
        {
            name = $"{nameof(MainMenuTitleVideo)} frames"
        };

        frames.Create();
        return true;
    }

    private void StartClip()
    {
        // Settled before Prepare, never after: a player that has already
        // prepared its output does not reliably take a new one, and the way
        // that fails is a clip that plays into nothing.
        player.clip = clip;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = frames;

        // A logo has nothing to say. The menu has its own music and this would
        // only be a second thing starting up over it.
        player.audioOutputMode = VideoAudioOutputMode.None;

        player.isLooping = true;
        player.waitForFirstFrame = true;

        player.prepareCompleted += HandlePrepared;
        player.errorReceived += HandleError;

        player.Prepare();
    }

    // Only now is the name replaced. Doing it when the clip was handed over
    // would leave an empty masthead for however long the file takes to open,
    // and for ever if it never does.
    private void HandlePrepared(VideoPlayer source)
    {
        source.Play();
        isShowingFilm = true;
        ShowFilm();
    }

    private void ShowFilm()
    {
        if (title == null || frames == null)
            return;

        title.text = string.Empty;
        title.style.backgroundImage = Background.FromRenderTexture(frames);
        title.style.height = displayHeight;
        title.style.width = displayHeight * clip.width / (float)clip.height;
    }

    private void HandleError(VideoPlayer source, string message)
    {
        Debug.LogWarning(
            $"{nameof(MainMenuTitleVideo)} could not play its clip: {message}",
            this);

        RestoreText();
    }

    private void RestoreText()
    {
        isShowingFilm = false;

        if (title == null)
            return;

        title.style.backgroundImage = StyleKeyword.Null;
        title.style.width = StyleKeyword.Null;
        title.style.height = StyleKeyword.Null;
        title.text = fallbackText;
    }

    private void ReleaseFrames()
    {
        if (frames == null)
            return;

        frames.Release();
        Destroy(frames);
        frames = null;
    }
}
