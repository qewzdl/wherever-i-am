using UnityEngine;
using UnityEngine.UIElements;

// The pause screen, in UI Toolkit. Same contract as the uGUI one it replaces:
// the scene feature constructs it, the pause service drives it, and the local
// player's input is switched off while it is up.
//
// What changed is only the view. Looks live in the theme, so this class holds
// no colours and no sizes - which is the whole point of the move. The settings
// screen is not owned here either: it belongs to the whole game, and this only
// asks it to open.
[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class PauseMenuDocument : MonoBehaviour, IPauseServiceConsumer
{
    [Header("References")]
    [SerializeField] private UIDocument document;
    [SerializeField] private HUDUI hudUI;
    [SerializeField] private UiDocumentSounds sounds;

    private IPauseService pauseService;
    private ISettingsScreen settingsScreen;
    private INetworkSessionService sessionService;
    private IPlayerScopeRegistry playerScopes;
    private ILocalPlayerInputService localInputService;

    private const string OpenClass = "screen--open";

    // Matches --open-time in the theme. Two places for one number is a cost;
    // the alternative is reading a resolved style before the first layout,
    // which is not available when this runs.
    // Longer than --motion-screen (0.26s), which is what the screen's opacity
    // actually takes. Anything shorter switches display off mid-fade and the
    // screen vanishes at half brightness - the one animation in the interface
    // nobody could work out why it looked broken.
    private const long FadeMilliseconds = 300;

    private VisualElement screen;
    private IVisualElementScheduledItem hideAfterFade;
    private Button resumeButton;
    private Button settingsButton;
    private Button mainMenuButton;
    private Button quitButton;

    private void Awake()
    {
        if (document == null)
            document = GetComponent<UIDocument>();

        if (sounds == null)
            sounds = GetComponent<UiDocumentSounds>();
    }

    // Harmless before the scene feature constructs it: an invisible screen that
    // still swallows clicks reads as a fault in whatever it happens to cover.
    private void OnEnable()
    {
        if (TryBindDocument())
            SetScreenVisible(false);
    }

    public void Construct(
        IPauseService pauseService,
        INetworkSessionService sessionService,
        IPlayerScopeRegistry playerScopeRegistry,
        ISettingsScreen settingsScreen)
    {
        Unsubscribe();

        this.pauseService = pauseService;
        this.sessionService = sessionService;
        this.settingsScreen = settingsScreen;
        playerScopes = playerScopeRegistry;

        if (!TryBindDocument())
            return;

        Subscribe();
        RefreshLocalInputService();
        HandlePauseStateChanged(this.pauseService != null && this.pauseService.IsPaused);
    }

    public void BindPauseService(IPauseService pauseService)
    {
        Construct(pauseService, sessionService, playerScopes, settingsScreen);
    }

    public void Dispose()
    {
        if (pauseService != null && pauseService.IsPaused)
            SetPlayerInputActive(true);

        Unsubscribe();
        Hide();

        pauseService = null;
        sessionService = null;
        settingsScreen = null;
        playerScopes = null;
        localInputService = null;
    }

    private void OnDestroy()
    {
        Dispose();
    }

    private bool TryBindDocument()
    {
        if (document == null)
            document = GetComponent<UIDocument>();

        VisualElement root = document != null ? document.rootVisualElement : null;

        if (root == null)
        {
            Debug.LogError(
                $"{nameof(PauseMenuDocument)} has no {nameof(UIDocument)} to bind.",
                this);
            return false;
        }

        screen = root.Q<VisualElement>("Screen");
        resumeButton = root.Q<Button>("ResumeButton");
        settingsButton = root.Q<Button>("SettingsButton");
        mainMenuButton = root.Q<Button>("MainMenuButton");
        quitButton = root.Q<Button>("QuitButton");

        if (screen != null)
            return true;

        Debug.LogError(
            $"{nameof(PauseMenuDocument)} did not find 'Screen' in its document.",
            this);
        return false;
    }

    private void Subscribe()
    {
        if (resumeButton != null)
            resumeButton.clicked += Resume;

        if (settingsButton != null)
            settingsButton.clicked += OpenSettings;

        if (mainMenuButton != null)
            mainMenuButton.clicked += ReturnToMainMenu;

        if (quitButton != null)
            quitButton.clicked += QuitGame;

        if (pauseService != null)
            pauseService.PauseStateChanged += HandlePauseStateChanged;

        if (playerScopes != null && !playerScopes.IsDisposed)
        {
            playerScopes.PlayerScopeOpened += HandlePlayerScopeOpened;
            playerScopes.PlayerScopeClosing += HandlePlayerScopeClosing;
        }
    }

    private void Unsubscribe()
    {
        if (resumeButton != null)
            resumeButton.clicked -= Resume;

        if (settingsButton != null)
            settingsButton.clicked -= OpenSettings;

        if (mainMenuButton != null)
            mainMenuButton.clicked -= ReturnToMainMenu;

        if (quitButton != null)
            quitButton.clicked -= QuitGame;

        if (pauseService != null)
            pauseService.PauseStateChanged -= HandlePauseStateChanged;

        if (playerScopes != null)
        {
            playerScopes.PlayerScopeOpened -= HandlePlayerScopeOpened;
            playerScopes.PlayerScopeClosing -= HandlePlayerScopeClosing;
        }
    }

    private void HandlePauseStateChanged(bool isPaused)
    {
        if (isPaused)
            Show();
        else
            Hide();

        SetPlayerInputActive(!isPaused);
    }

    private void Resume()
    {
        pauseService?.Resume();
    }

    private void OpenSettings()
    {
        if (settingsScreen != null)
            settingsScreen.Open();
    }

    private void ReturnToMainMenu()
    {
        sessionService?.ShutdownToMainMenu();
    }

    private void QuitGame()
    {
        Application.Quit();
    }

    // Hidden by display rather than by switching the document off: a disabled
    // UIDocument drops its element tree, and every query above would have to be
    // repeated on the way back.
    private void Show()
    {
        SetScreenVisible(true);

        if (sounds != null)
            sounds.Play(UiSoundType.Open);

        if (hudUI != null)
            hudUI.HideHUD();
    }

    private void Hide()
    {
        // Only when it was actually up. Hiding happens on construction and on
        // teardown as well, and a pause sound on entering the level would be a
        // strange way to start a match.
        bool wasOpen = screen != null && screen.ClassListContains(OpenClass);

        // Settings open from here, so they close from here too. Without this a
        // player who unpauses with the key walks away with the settings screen
        // still standing over the game.
        if (settingsScreen != null)
            settingsScreen.Close();

        SetScreenVisible(false);

        if (wasOpen && sounds != null)
            sounds.Play(UiSoundType.Close);

        if (hudUI != null)
            hudUI.ShowHUD();
    }

    // The fade lives in the stylesheet; this only says which side of it we are
    // on. Display is still switched, because an invisible screen that still
    // swallows clicks is worse than no animation - but on the way out it waits
    // for the fade instead of cutting it off.
    private void SetScreenVisible(bool visible)
    {
        if (screen == null)
            return;

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

    private void SetPlayerInputActive(bool value)
    {
        localInputService?.SetInputActive(this, value);
    }

    private void HandlePlayerScopeOpened(IPlayerScope playerScope)
    {
        if (playerScope == null || !playerScope.IsLocalPlayer)
            return;

        RefreshLocalInputService();
        SetPlayerInputActive(pauseService == null || !pauseService.IsPaused);
    }

    private void HandlePlayerScopeClosing(IPlayerScope playerScope)
    {
        if (playerScope == null || !playerScope.IsLocalPlayer)
            return;

        localInputService?.SetInputActive(this, true);
        localInputService = null;
    }

    private void RefreshLocalInputService()
    {
        localInputService = null;

        if (playerScopes == null ||
            playerScopes.IsDisposed ||
            !playerScopes.TryGetLocalPlayerScope(out IPlayerScope playerScope) ||
            playerScope.LocalServices == null)
        {
            return;
        }

        playerScope.LocalServices.TryResolve(out localInputService);
    }
}
