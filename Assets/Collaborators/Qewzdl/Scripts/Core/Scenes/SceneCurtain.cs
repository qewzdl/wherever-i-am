using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// The dark every listed scene arrives out of.
//
// It began as an element inside the main menu's own markup, which worked and
// did not scale: the lobby would have needed its own copy, and the match has
// no full-screen document to put one in at all. So it is one panel, made here,
// living above everything and outliving every scene.
//
// The panel is built rather than authored. A UIDocument in the bootstrap scene
// would have needed a second PanelSettings asset for the sorting order alone,
// and a curtain is one element with one class - there is nothing for a markup
// file to say about it that this does not say in a line.
[DisallowMultipleComponent]
public sealed class SceneCurtain : MonoBehaviour
{
    private const string CurtainClass = "curtain";

    // What UiPreferences puts on a tree when the player has asked for less
    // movement. Read rather than relied on through a stylesheet, because the
    // fade is driven here now and a token it never reads would be a setting
    // that silently stopped working.
    private const string ReducedMotionClass = "motion--reduced";

    // Above the settings screen, the error overlay and the film, which sit at
    // one, two and three. This is the last thing between the player and the
    // game, so nothing is drawn over it.
    private const int SortingOrder = 1000;

    // A frame this long or shorter counts as the scene having settled. Thirty
    // a second rather than sixty: the point is to tell a machine doing work
    // from a machine that is merely slow, and a slow machine still deserves
    // its menu.
    private const float SteadyFrameSeconds = 1f / 30f;

    // Consecutive, because the first cheap frame after a hitch is usually a
    // lull between two expensive ones.
    private const int SteadyFramesWanted = 5;

    // And a limit on the waiting, because a machine that never manages five
    // quiet frames in a row would otherwise sit looking at black for ever.
    private const float LongestHold = 2.5f;

    [Tooltip("Which scenes open with the lightening.")]
    [SerializeField] private SceneCurtainConfig config;

    [Tooltip("Cloned rather than used: the copy is the same interface at a " +
             "sorting order of its own, so raising this one does not raise " +
             "every screen in the game with it.")]
    [SerializeField] private PanelSettings panelSettings;

    private IProjectSceneRegistry sceneRegistry;
    private PanelSettings ownPanel;
    private UIDocument document;
    private VisualElement root;
    private VisualElement curtain;
    private float span;
    private float left;
    private float held;
    private int steady;
    private bool settled;
    private bool listening;

    public bool Initialize()
    {
        if (config == null || panelSettings == null)
        {
            Debug.LogError(
                $"{nameof(SceneCurtain)} has no config or no panel settings, " +
                "so no scene would ever be covered.",
                this);

            return false;
        }

        Build();
        return root != null;
    }

    public void Construct(IProjectSceneRegistry registry)
    {
        sceneRegistry = registry ?? throw new ArgumentNullException(nameof(registry));

        if (listening)
            return;

        SceneManager.sceneLoaded += HandleSceneLoaded;
        listening = true;
    }

    public void Release()
    {
        if (listening)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            listening = false;
        }

        sceneRegistry = null;
    }

    private void OnDestroy()
    {
        Release();

        if (ownPanel != null)
            Destroy(ownPanel);
    }

    private void Build()
    {
        ownPanel = Instantiate(panelSettings);
        ownPanel.name = $"{panelSettings.name} (Curtain)";
        ownPanel.sortingOrder = SortingOrder;

        GameObject host = new(nameof(SceneCurtain));
        host.transform.SetParent(transform, false);

        document = host.AddComponent<UIDocument>();
        document.panelSettings = ownPanel;

        root = document.rootVisualElement;

        // Nothing under here is ever typed into or pressed, and a full-screen
        // element that took the pointer would make the whole game unclickable
        // for as long as it lasted - including for ever, if a lift were ever
        // missed.
        root.pickingMode = PickingMode.Ignore;

        // The scale and the motion the player asked for, the same as every
        // other tree gets. It is what carries motion--reduced, which is how
        // the accessibility setting reaches the fade.
        UiPreferences.Attach(root);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // An additive load is something arriving inside the scene the player
        // is already in - a map, a room - and covering the screen for that
        // would be covering the thing they are looking at.
        if (mode != LoadSceneMode.Single || root == null || sceneRegistry == null)
            return;

        if (!config.TryGetSeconds(sceneRegistry.GetSceneKind(scene.name), out float seconds))
            return;

        Drop(seconds);
    }

    // Driven here rather than by a USS transition.
    //
    // A transition needs a resolved style to leave from, and an element made
    // in the frame a scene loads has never been laid out - so the class that
    // was meant to start the fade only ever set the end value. Every way round
    // that failed in a different place: a scheduled frame, the first geometry,
    // an inline duration cleared a frame later, a class that turned the
    // transition off and on again. The last of those collapsed into a single
    // frame because the scheduler runs an item added during its own pass in
    // that same pass.
    //
    // None of it could be measured either: a batchmode run does not render,
    // and without rendering a transition never advances, so the test harness
    // could not tell a three second fade from an instant one. An opacity
    // written here every frame is the same fade, works whether or not anything
    // has been laid out, and can be measured by anybody who can read a number.
    private void Drop(float seconds)
    {
        curtain?.RemoveFromHierarchy();

        curtain = new VisualElement { pickingMode = PickingMode.Ignore };
        curtain.AddToClassList(CurtainClass);
        curtain.style.opacity = 1f;
        root.Add(curtain);

        span = root.ClassListContains(ReducedMotionClass) ? 0f : seconds;
        left = span;
        held = 0f;
        steady = 0;
        settled = false;

        if (span <= 0f)
            Finish();
    }

    // Unscaled, because a scene arriving is exactly when something else is
    // likely to have stopped the clock.
    private void Update()
    {
        if (curtain == null || span <= 0f)
            return;

        // Black until the scene has stopped struggling.
        //
        // The first visit to a scene is shaders compiling, assets arriving and
        // atlases being built, and those frames are hundreds of milliseconds
        // long. A fade counted in real time across them does not fade - it
        // jumps, three or four times, and then it is over. The second visit is
        // warm and looks fine, which is exactly what was reported.
        //
        // Covering that work is what a curtain is for, so it waits for it
        // rather than fading over it.
        if (!settled && !HasSettled())
            return;

        left -= Time.unscaledDeltaTime;

        if (left <= 0f)
        {
            Finish();
            return;
        }

        curtain.style.opacity = left / span;
    }

    private bool HasSettled()
    {
        held += Time.unscaledDeltaTime;

        steady = Time.unscaledDeltaTime <= SteadyFrameSeconds ? steady + 1 : 0;
        settled = steady >= SteadyFramesWanted || held >= LongestHold;

        return settled;
    }

    // Taken out of the tree rather than left at zero. It is one full-screen
    // element per scene load, and a game that has been played for an hour
    // should not be carrying an hour of them.
    private void Finish()
    {
        curtain?.RemoveFromHierarchy();
        curtain = null;
        span = 0f;
        left = 0f;
    }
}
