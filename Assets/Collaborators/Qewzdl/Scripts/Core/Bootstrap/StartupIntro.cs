using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

// The film that runs while the game finishes standing up.
//
// Where it sits is worth writing down. AppRuntime builds the whole project
// context first - services, settings, audio, the network manager - and only
// then asks for the startup scene. So by the time this plays, the expensive
// half of starting the game has already happened and what is left is one scene
// load. The film is not hiding a wait; it is filling the one that was there.
//
// It stands above everything: the settings screen draws at sorting order 1 and
// the error overlay at 2, and this is 3, because while it is up it is the only
// thing there is.
//
// The startup scene is let go the moment the film ends, and the fade begins
// after that - so the menu builds itself behind a panel that is still opaque
// and is uncovered with its own entrance already running. Which is also why
// this object outlives a scene load: it has to outlive the thing it covers.
//
// Not skippable, by decision. If that changes it is a few lines, but they are
// lines that need the input package - this project runs the new backend only,
// so the one-liner everybody reaches for first would throw - and an untested
// way out of a screen with no other way out is worse than none.
[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class StartupIntro : MonoBehaviour, IStartupIntro
{
    private const string GoneClass = "intro--gone";

    [Header("References")]
    [SerializeField] private UIDocument document;
    [SerializeField] private VideoClip clip;

    [Header("Playback")]
    [Tooltip("Volume of the clip's own audio track. Its own number rather " +
             "than the master volume: on a first launch nobody has been able " +
             "to reach the settings screen yet.")]
    [SerializeField, Range(0f, 1f)] private float volume = 1f;

    [Tooltip("Off while working on a scene directly, so the film is not " +
             "watched forty times an afternoon.")]
    [SerializeField] private bool playInEditor = true;

    [Header("Giving up")]
    [Tooltip("How long to wait for the clip to open before carrying on " +
             "without it. A missing codec must not become a black screen " +
             "with no way out of it.")]
    [SerializeField, Min(1f)] private float prepareTimeoutSeconds = 5f;

    [Tooltip("Added to the clip's own length before giving up on it ending. " +
             "Covers a slow first frame, not a broken file.")]
    [SerializeField, Min(1f)] private float playbackSlackSeconds = 3f;

    [Tooltip("Matches --motion-screen in the theme, which is what the class " +
             "on the panel actually animates.")]
    [SerializeField, Min(0f)] private float fadeSeconds = 0.26f;

    private VideoPlayer player;
    private RenderTexture frames;
    private Action completed;
    private VisualElement boundRoot;
    private VisualElement screen;
    private VisualElement surface;
    private Coroutine timeout;
    private bool finished;

    private void Awake()
    {
        if (document == null)
            document = GetComponent<UIDocument>();

        // Made here rather than placed in the scene. Every setting on it that
        // matters is written a few lines below anyway, so a serialised copy
        // would only be somewhere for those to be quietly different.
        player = gameObject.AddComponent<VideoPlayer>();
        player.playOnAwake = false;

        // Only a root survives, and this has to: the scene load it sets off
        // would otherwise take it away mid-fade and cut to the menu on the
        // frame the film ended.
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    // UIDocument builds its tree in its own OnEnable and component order on one
    // object is not something to rely on, so being too early here is the
    // ordinary case rather than a fault - the same reason the error overlay
    // binds in three places and complains in none of them.
    //
    // Binding only. These used to put the panel away as well, and that was
    // exactly backwards: AppRuntime runs at -1000, so it has already asked for
    // the film and the panel is already up by the time Start gets here. The
    // intro showed itself and then hid itself on the same frame, which looked
    // from the outside like an empty scene with a soundtrack.
    //
    // Nothing has to hide it anyway. The panel is display: none in the
    // stylesheet and only TryPlay ever says otherwise.
    private void OnEnable()
    {
        Bind(complainIfMissing: false);
    }

    private void Start()
    {
        Bind(complainIfMissing: false);
    }

    public bool TryPlay(Action onCompleted)
    {
        if (onCompleted == null)
            throw new ArgumentNullException(nameof(onCompleted));

        if (!CanPlay())
            return false;

        if (!Bind(complainIfMissing: true))
            return false;

        completed = onCompleted;
        SetVisible(true);

        // Everything about where the picture goes is settled before Prepare,
        // never after. A player that has already prepared its output does not
        // reliably take a new target, and the way that fails is the way this
        // failed twice: the soundtrack plays and nothing is ever written into
        // the texture the panel is showing.
        //
        // The size comes off the clip asset, which knows it without anybody
        // opening anything - so there is nothing to wait for and no reason to
        // change the player's mind later.
        if (!TryMakeFrames())
        {
            Finish();
            return true;
        }

        player.clip = clip;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = frames;
        player.audioOutputMode = VideoAudioOutputMode.Direct;
        player.isLooping = false;
        player.waitForFirstFrame = true;

        player.prepareCompleted += HandlePrepared;
        player.loopPointReached += HandleEnded;
        player.errorReceived += HandleError;

        player.Prepare();
        timeout = StartCoroutine(GiveUpIfItNeverOpens());

        // Taken, whatever happens next. Every way out of here goes through
        // Finish, and Finish is what lets the game carry on.
        return true;
    }

    private bool CanPlay()
    {
        if (finished || completed != null)
            return false;

        if (clip == null || document == null || player == null)
            return false;

#if UNITY_EDITOR
        if (!playInEditor)
            return false;
#endif

        return true;
    }

    // Somewhere for the frames to be, and it has to be a RenderTexture: that
    // is the only kind of live texture a panel's background can be made from.
    //
    // Made at the clip's own size, which is what makes the letterboxing come
    // out right - the panel then only has to fit a picture already of the
    // correct shape, rather than guess at one.
    private bool TryMakeFrames()
    {
        int width = clip != null ? (int)clip.width : 0;
        int height = clip != null ? (int)clip.height : 0;

        if (surface == null || width <= 0 || height <= 0)
        {
            Debug.LogWarning(
                $"{nameof(StartupIntro)} has no picture to show " +
                $"({width}x{height}).",
                this);

            return false;
        }

        frames = new RenderTexture(width, height, 0)
        {
            name = $"{nameof(StartupIntro)} frames"
        };

        frames.Create();
        surface.style.backgroundImage = Background.FromRenderTexture(frames);
        return true;
    }

    private void HandlePrepared(VideoPlayer source)
    {
        if (finished)
            return;

        StopTimeout();
        source.SetDirectAudioVolume(0, Mathf.Clamp01(volume));
        source.Play();

        timeout = StartCoroutine(GiveUpIfItNeverEnds());
    }

    private void HandleEnded(VideoPlayer source)
    {
        Finish();
    }

    private void HandleError(VideoPlayer source, string message)
    {
        Debug.LogWarning(
            $"{nameof(StartupIntro)} could not play its clip: {message}",
            this);

        Finish();
    }

    private IEnumerator GiveUpIfItNeverOpens()
    {
        float deadline = Time.realtimeSinceStartup + prepareTimeoutSeconds;

        while (!player.isPrepared && Time.realtimeSinceStartup < deadline)
            yield return null;

        timeout = null;

        if (player.isPrepared || finished)
            yield break;

        Debug.LogWarning(
            $"{nameof(StartupIntro)} gave up waiting for its clip to open.",
            this);

        Finish();
    }

    // The clip's own length, and then some. Opening a file was guarded from
    // the start and playing it was not, which left one way for this to hold
    // the game forever: a clip that opens, plays, and never reaches its end -
    // a stalled decoder, a file that lies about its length, a frame that never
    // arrives. There is no other way out of this screen, so there has to be
    // this one.
    private IEnumerator GiveUpIfItNeverEnds()
    {
        double length = clip != null ? clip.length : 0d;
        float deadline = Time.realtimeSinceStartup +
                         (float)length +
                         Mathf.Max(1f, playbackSlackSeconds);

        while (!finished && Time.realtimeSinceStartup < deadline)
            yield return null;

        timeout = null;

        if (finished)
            yield break;

        Debug.LogWarning(
            $"{nameof(StartupIntro)} gave up waiting for its clip to end.",
            this);

        Finish();
    }

    // The game first, the curtain second.
    private void Finish()
    {
        if (finished)
            return;

        finished = true;
        StopTimeout();
        Unsubscribe();

        if (player != null && player.isPlaying)
            player.Stop();

        Action carryOn = completed;
        completed = null;
        carryOn?.Invoke();

        StartCoroutine(FadeOutAndLeave());
    }

    private IEnumerator FadeOutAndLeave()
    {
        screen?.AddToClassList(GoneClass);

        // Unscaled: nothing has set a time scale yet, and a splash that waits
        // on game time is a splash that never ends if something ever does.
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, fadeSeconds));

        Destroy(gameObject);
    }

    private bool Bind(bool complainIfMissing)
    {
        if (document == null)
            document = GetComponent<UIDocument>();

        VisualElement root = document != null ? document.rootVisualElement : null;

        if (root == null)
            return false;

        if (ReferenceEquals(root, boundRoot))
            return true;

        VisualElement foundScreen = root.Q<VisualElement>("Intro");
        VisualElement foundSurface = root.Q<VisualElement>("IntroVideo");

        // Remembered only once it worked. A tree that is not built yet is the
        // ordinary case on the first frame, and caching that failure would
        // leave this mute for the rest of the run.
        if (foundScreen == null || foundSurface == null)
        {
            if (complainIfMissing)
                Debug.LogError($"{nameof(StartupIntro)} did not find its panel.", this);

            return false;
        }

        boundRoot = root;
        screen = foundScreen;
        surface = foundSurface;
        return true;
    }

    // Display rather than opacity, because a transparent panel still takes
    // every click aimed past it - and this one covers the whole screen.
    private void SetVisible(bool visible)
    {
        if (screen == null)
            return;

        screen.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        screen.pickingMode = visible ? PickingMode.Position : PickingMode.Ignore;
    }

    private void StopTimeout()
    {
        if (timeout == null)
            return;

        StopCoroutine(timeout);
        timeout = null;
    }

    // Made by hand, so let go of by hand: a RenderTexture is not collected
    // with the component that happened to be holding it.
    private void ReleaseFrames()
    {
        if (frames == null)
            return;

        if (player != null && player.targetTexture == frames)
            player.targetTexture = null;

        frames.Release();
        Destroy(frames);
        frames = null;
    }

    private void Unsubscribe()
    {
        if (player == null)
            return;

        player.prepareCompleted -= HandlePrepared;
        player.loopPointReached -= HandleEnded;
        player.errorReceived -= HandleError;
    }

    private void OnDestroy()
    {
        StopTimeout();
        Unsubscribe();
        ReleaseFrames();

        // Torn down before the film ended - the editor stopping, the
        // application quitting - and the game is still owed its startup scene.
        // Without this, a run that is interrupted here never loads anything.
        Action carryOn = completed;
        completed = null;
        carryOn?.Invoke();
    }
}
