using UnityEngine;
using UnityEngine.UIElements;

// The one place the game says something went wrong, in UI Toolkit.
//
// It lives in Bootstrap and outlives every scene, because the errors worth a
// panel are the ones that happen between scenes: a host that would not start,
// a join that timed out, a session that fell over. Being global is also why it
// draws itself rather than borrowing a screen - there is no scene to borrow
// from while one is being loaded.
//
// The view used to be a uGUI prefab instantiated on first use. It is a
// document now, and there is no prefab and no second class: showing an error
// is setting a label and adding a class.
[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class UiErrorManager : MonoBehaviour, IUiErrorService
{
    private const string DefaultErrorMessage = "Unknown error.";
    private const string OpenClass = "screen--open";

    // Matches --motion-screen in the theme. Two places for one number is a
    // cost; reading a resolved style before the first layout is not available
    // when this runs.
    // Longer than --motion-screen (0.26s), which is what the screen's opacity
    // actually takes. Anything shorter switches display off mid-fade and the
    // screen vanishes at half brightness - the one animation in the interface
    // nobody could work out why it looked broken.
    private const long FadeMilliseconds = 300;

    [SerializeField] private UIDocument document;

    // Its own two sounds, and nothing else: handing audio to other components
    // was never this class's business.
    private IUiSoundService uiSoundService;

    private VisualElement boundRoot;
    private VisualElement screen;
    private Label errorText;
    private Button closeButton;
    private IVisualElementScheduledItem hideAfterFade;
    private bool isShown;

    private void Awake()
    {
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        if (document == null)
            document = GetComponent<UIDocument>();
    }

    // A screen covers everything and takes the pointer with it, so it has to be
    // harmless before anybody shows an error - which is most of the time.
    private void OnEnable()
    {
        if (Bind(complainIfMissing: false))
            SetVisible(false);
    }

    // UIDocument builds its tree in its own OnEnable, and component order on
    // one object is not something to rely on.
    private void Start()
    {
        if (Bind(complainIfMissing: false))
            SetVisible(false);
    }

    public void Construct(IAudioService service)
    {
        uiSoundService = service != null ? service.UI : null;
    }

    public void DisposeComposition()
    {
        uiSoundService = null;
    }

    private void OnDestroy()
    {
        Unsubscribe();
        DisposeComposition();
    }

    public void ShowError(string message)
    {
        // The one place that complains: hiding early is harmless, but an error
        // nobody can see is worse than a line in the log.
        if (!Bind(complainIfMissing: true))
            return;

        errorText.text = string.IsNullOrWhiteSpace(message)
            ? DefaultErrorMessage
            : message;

        SetVisible(true);

        // OK is the only thing here, so it takes the focus and Enter works.
        if (closeButton != null)
            closeButton.schedule.Execute(() => closeButton.Focus());
        uiSoundService?.PlayError();
    }

    public void HideError()
    {
        if (!isShown)
            return;

        SetVisible(false);
        uiSoundService?.PlayClose();
    }

    private bool Bind(bool complainIfMissing)
    {
        if (document == null)
            document = GetComponent<UIDocument>();

        VisualElement root = document != null ? document.rootVisualElement : null;

        if (root == null)
            return false;

        // Binding subscribes, so doing it twice would close the panel twice on
        // one click; and a document that is switched off rebuilds its tree,
        // which makes the old references stale rather than merely duplicated.
        if (ReferenceEquals(root, boundRoot))
            return true;

        VisualElement found = root.Q<VisualElement>("Screen");
        Label foundText = root.Q<Label>("ErrorText");

        // Remembered only once it worked. A tree that is not built yet is the
        // ordinary case on the first frame, and caching the failure would leave
        // the manager mute for the rest of the run.
        if (found == null || foundText == null)
        {
            if (complainIfMissing)
            {
                Debug.LogError(
                    $"{nameof(UiErrorManager)} did not find its screen in the document.",
                    this);
            }

            return false;
        }

        boundRoot = root;

        // Whatever the player set for the interface as a whole - its scale, its
        // text size, whether it moves - applies to this tree too, and applies
        // now rather than the next time they open the settings screen.
        UiPreferences.Attach(root);
        screen = found;
        errorText = foundText;
        closeButton = root.Q<Button>("CloseButton");

        if (closeButton != null)
            closeButton.clicked += HideError;

        // Anywhere outside the panel dismisses it as well, the way the uGUI
        // one did through a full-screen button behind it. Only the veil itself
        // counts: a click that started on the panel bubbles up to here too.
        screen.RegisterCallback<ClickEvent>(HandleBackdropClicked);

        // And the key that closes windows, which is what this is.
        screen.RegisterCallback<NavigationCancelEvent>(HandleCancelPressed);
        return true;
    }

    private void Unsubscribe()
    {
        if (closeButton != null)
            closeButton.clicked -= HideError;

        screen?.UnregisterCallback<ClickEvent>(HandleBackdropClicked);
        screen?.UnregisterCallback<NavigationCancelEvent>(HandleCancelPressed);
    }

    private void HandleBackdropClicked(ClickEvent evt)
    {
        if (ReferenceEquals(evt.target, screen))
            HideError();
    }

    private void HandleCancelPressed(NavigationCancelEvent evt)
    {
        if (!isShown)
            return;

        // An error can stand over a paused game, and the pause menu reads the
        // escape key directly. Without this, dismissing one unpauses.
        PauseMenuInput.SuppressToggleForCurrentFrame();

        HideError();
        evt.StopPropagation();
    }

    // The fade lives in the stylesheet; this only says which side of it we are
    // on. Display is still switched, because an invisible screen that still
    // swallows clicks is worse than no animation - but on the way out it waits
    // for the fade instead of cutting it off.
    private void SetVisible(bool visible)
    {
        if (screen == null)
            return;

        isShown = visible;
        screen.pickingMode = visible ? PickingMode.Position : PickingMode.Ignore;
        hideAfterFade?.Pause();
        hideAfterFade = null;

        if (visible)
        {
            screen.style.display = DisplayStyle.Flex;

            // A class added in the same frame as display never transitions:
            // the element goes from "not laid out" straight to its end state.
            screen.schedule.Execute(() => screen.AddToClassList(OpenClass));
            return;
        }

        screen.RemoveFromClassList(OpenClass);
        hideAfterFade = screen.schedule
            .Execute(() => screen.style.display = DisplayStyle.None)
            .StartingIn(FadeMilliseconds);
    }
}
