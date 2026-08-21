using UnityEngine;
using UnityEngine.UIElements;

// The main menu, in UI Toolkit. Same contract as the uGUI one it replaces: the
// scene feature constructs it, the session service does the connecting, and
// errors go to the error service.
//
// What changed is the view and one thing about the shape. The address is asked
// for when it is needed rather than kept on screen: a player about to host has
// no use for it, and a menu is easier to read when it only shows what it is
// about to use. The name stays out in the open, because both paths carry it.
[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class MainMenuDocument : MonoBehaviour
{
    private const string OpenClass = "screen--open";
    private const int NameLengthLimit = 16;

    [Header("References")]
    [SerializeField] private UIDocument document;
    [SerializeField] private UiDocumentSounds sounds;

    [Header("While connecting")]
    [SerializeField] private string hostingMessage = "Creating lobby...";
    [SerializeField] private string joiningMessage = "Connecting to {0}...";
    [SerializeField] private string cancellingMessage = "Cancelling...";

    // What is being waited for, and for how long. A connection that is going
    // nowhere looks exactly like one that is about to arrive, and the player
    // deciding whether to press Cancel has nothing else to go on.
    [SerializeField] private string busyElapsedFormat = "{0}   {1} s";

    private INetworkSessionService sessionService;
    private IUiErrorService errorService;
    private ISettingsScreen settingsScreen;

    private VisualElement boundRoot;
    private VisualElement screen;
    private VisualElement panel;
    private VisualElement joinPanel;
    private VisualElement busyPanel;
    private Label busyText;
    private TextField playerName;
    private TextField address;
    private Button hostButton;
    private Button joinButton;
    private Button settingsButton;
    private Button quitButton;
    private Button connectButton;
    private Button cancelJoinButton;
    private Button cancelRequestButton;

    // A click handler that awaits hands the click straight back at the first
    // await. The session service does refuse the second attempt, but only after
    // it has been made - and on a join that is timing out, that is a screenful
    // of errors for a player doing the obvious thing and clicking again.
    // Hosting and joining share the flag: both end in one session, and starting
    // one while the other is in flight is the same mistake.
    private bool isRequestInFlight;

    // Cancelling is itself a request that takes time, and pressing Cancel twice
    // would start a second shutdown over the first.
    private bool isCancelling;

    private string busyMessage = string.Empty;
    private float requestStartedAt;

    // The last whole second put on screen. Without it the label is rebuilt
    // every frame to say what it already said.
    private int shownSeconds = -1;

    public bool IsRequestInFlight => isRequestInFlight;

    private void Awake()
    {
        if (document == null)
            document = GetComponent<UIDocument>();

        if (sounds == null)
            sounds = GetComponent<UiDocumentSounds>();
    }

    // Quiet here on purpose: UIDocument builds its tree in its own OnEnable,
    // and component order on one object is not something to rely on, so being
    // too early is expected rather than wrong.
    private void OnEnable()
    {
        Show(complainIfMissing: false);
    }

    // By Start the scene has finished waking up, so a document with nothing in
    // it is a fault worth saying out loud.
    private void Start()
    {
        Show(complainIfMissing: true);
    }

    public void Construct(
        INetworkSessionService sessionService,
        IUiErrorService errorService,
        ISettingsScreen settingsScreen)
    {
        this.sessionService = sessionService;
        this.errorService = errorService;
        this.settingsScreen = settingsScreen;

        Show(complainIfMissing: false);
    }

    public void Dispose()
    {
        sessionService = null;
        errorService = null;
        settingsScreen = null;
        EndRequest();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        Dispose();
    }

    // The clock runs from the moment the request started, not from the last
    // thing that happened to it: cancelling keeps counting, because what the
    // player is waiting on is still the same wait.
    private void Update()
    {
        if (!isRequestInFlight || busyText == null)
            return;

        int seconds = Mathf.FloorToInt(Time.unscaledTime - requestStartedAt);

        if (seconds == shownSeconds)
            return;

        shownSeconds = seconds;
        busyText.text = string.Format(busyElapsedFormat, busyMessage, seconds);
    }

    private void Show(bool complainIfMissing)
    {
        if (!Bind(complainIfMissing) || screen == null)
            return;

        // A class added in the same frame as the tree never transitions: the
        // element goes from "not laid out" straight to its end state.
        screen.schedule.Execute(() => screen.AddToClassList(OpenClass));
    }

    private bool Bind(bool complainIfMissing)
    {
        if (document == null)
            document = GetComponent<UIDocument>();

        VisualElement root = document != null ? document.rootVisualElement : null;

        if (root == null)
        {
            if (complainIfMissing)
                Debug.LogError($"{nameof(MainMenuDocument)} has no document to bind.", this);

            return false;
        }

        // Binding subscribes, so doing it twice would count every click twice;
        // and a document that is switched off rebuilds its tree, which makes
        // the old references stale rather than merely duplicated.
        if (ReferenceEquals(root, boundRoot))
            return screen != null;

        boundRoot = root;
        screen = root.Q<VisualElement>("Screen");
        panel = root.Q<VisualElement>("Panel");
        joinPanel = root.Q<VisualElement>("JoinPanel");
        busyPanel = root.Q<VisualElement>("BusyPanel");
        busyText = root.Q<Label>("BusyText");
        playerName = root.Q<TextField>("PlayerName");
        address = root.Q<TextField>("Address");
        hostButton = root.Q<Button>("HostButton");
        joinButton = root.Q<Button>("JoinButton");
        settingsButton = root.Q<Button>("SettingsButton");
        quitButton = root.Q<Button>("QuitButton");
        connectButton = root.Q<Button>("ConnectButton");
        cancelJoinButton = root.Q<Button>("CancelJoinButton");
        cancelRequestButton = root.Q<Button>("CancelRequestButton");

        if (screen == null)
        {
            if (complainIfMissing)
                Debug.LogError($"{nameof(MainMenuDocument)} did not find 'Screen'.", this);

            return false;
        }

        Subscribe();
        HideJoinPrompt();
        SetBusy(false, string.Empty);

        if (playerName != null)
        {
            playerName.maxLength = NameLengthLimit;
            playerName.SetValueWithoutNotify(PlayerNameProvider.Get());
        }

        address?.SetValueWithoutNotify(JoinAddressProvider.Get());

        return true;
    }

    private void Subscribe()
    {
        if (hostButton != null)
            hostButton.clicked += Host;

        if (joinButton != null)
            joinButton.clicked += ShowJoinPrompt;

        if (settingsButton != null)
            settingsButton.clicked += OpenSettings;

        if (quitButton != null)
            quitButton.clicked += Quit;

        if (connectButton != null)
            connectButton.clicked += Join;

        if (cancelJoinButton != null)
            cancelJoinButton.clicked += HideJoinPrompt;

        if (cancelRequestButton != null)
            cancelRequestButton.clicked += CancelRequest;

        // Saved when the field is left, and again before connecting: a player
        // who types a name and presses Create without leaving the field would
        // otherwise arrive as the last name they used, or as nobody. Not on
        // every keystroke, because storing it writes to disk.
        playerName?.RegisterCallback<FocusOutEvent>(HandleNameCommitted);
        address?.RegisterCallback<FocusOutEvent>(HandleAddressCommitted);
    }

    private void Unsubscribe()
    {
        if (hostButton != null)
            hostButton.clicked -= Host;

        if (joinButton != null)
            joinButton.clicked -= ShowJoinPrompt;

        if (settingsButton != null)
            settingsButton.clicked -= OpenSettings;

        if (quitButton != null)
            quitButton.clicked -= Quit;

        if (connectButton != null)
            connectButton.clicked -= Join;

        if (cancelJoinButton != null)
            cancelJoinButton.clicked -= HideJoinPrompt;

        if (cancelRequestButton != null)
            cancelRequestButton.clicked -= CancelRequest;

        playerName?.UnregisterCallback<FocusOutEvent>(HandleNameCommitted);
        address?.UnregisterCallback<FocusOutEvent>(HandleAddressCommitted);
    }

    private void HandleNameCommitted(FocusOutEvent evt)
    {
        SavePlayerName();
    }

    private void HandleAddressCommitted(FocusOutEvent evt)
    {
        SaveJoinAddress();
    }

    private void SavePlayerName()
    {
        if (playerName != null)
            PlayerNameProvider.Set(playerName.value);
    }

    private void SaveJoinAddress()
    {
        if (address != null)
            JoinAddressProvider.Set(address.value);
    }

    private void ShowJoinPrompt()
    {
        if (isRequestInFlight)
            return;

        SetDisplayed(joinPanel, true);
        sounds?.Play(UiSoundType.Open);
        address?.Focus();
    }

    private void HideJoinPrompt()
    {
        SetDisplayed(joinPanel, false);
    }

    private void OpenSettings()
    {
        if (settingsScreen != null)
        {
            settingsScreen.Open();
            return;
        }

        ShowError("Settings are unavailable.");
    }

    // Public because these two are what the menu does, and a test that asks
    // whether a second click is ignored should not have to build a panel to
    // ask it.
    public async void Host()
    {
        SavePlayerName();

        if (!TryBeginRequest(hostingMessage))
            return;

        try
        {
            if (!HasSessionService())
                return;

            await sessionService.HostLanAsync();
        }
        finally
        {
            EndRequest();
        }
    }

    public void Join()
    {
        SaveJoinAddress();
        Join(address != null ? address.value : string.Empty);
    }

    public async void Join(string host)
    {
        SavePlayerName();

        // Normalised here as well as on the way to storage: Join is public and
        // whatever reaches the session service should be what was stored.
        host = JoinAddressProvider.Normalize(host);

        if (!TryBeginRequest(string.Format(joiningMessage, host)))
            return;

        HideJoinPrompt();

        try
        {
            if (!HasSessionService())
                return;

            await sessionService.JoinLanAsync(host);
        }
        finally
        {
            EndRequest();
        }
    }

    private void Quit()
    {
        Application.Quit();
    }

    // A join to an address nobody is listening on takes as long as the
    // transport's timeout, which is long enough for a player to decide they
    // typed it wrong. Shutting the session down is what cancels the attempt:
    // the connection service already carries a cancellation token for exactly
    // this, and a cancelled attempt is the one failure the flow service does
    // not report as an error - because the player is the one who asked.
    //
    // Cancelling ends with the main menu being loaded again, the same way a
    // failed connection does. That is why nothing is restored here: what comes
    // back is a fresh menu, not this one.
    public async void CancelRequest()
    {
        if (!isRequestInFlight || isCancelling || sessionService == null)
            return;

        isCancelling = true;
        SetBusy(true, cancellingMessage);
        cancelRequestButton?.SetEnabled(false);

        try
        {
            await sessionService.ShutdownToMainMenuAsync();
        }
        finally
        {
            isCancelling = false;

            // The scene may have been reloaded under this object while the
            // shutdown ran, and a destroyed component has no screen to tidy.
            if (this != null)
            {
                cancelRequestButton?.SetEnabled(true);
                EndRequest();
            }
        }
    }

    // Released in a finally, so a service that throws leaves the menu usable
    // rather than dead until the scene reloads.
    private bool TryBeginRequest(string busyMessage)
    {
        if (isRequestInFlight)
            return false;

        isRequestInFlight = true;
        isCancelling = false;
        requestStartedAt = Time.unscaledTime;
        HideError();
        SetBusy(true, busyMessage);
        return true;
    }

    private void EndRequest()
    {
        isRequestInFlight = false;
        SetBusy(false, string.Empty);
    }

    // The whole panel is switched off rather than a named list of controls:
    // in uGUI that list had to be kept by hand, and anything left out of it
    // stayed clickable while the menu was busy.
    private void SetBusy(bool isBusy, string message)
    {
        SetDisplayed(busyPanel, isBusy);
        panel?.SetEnabled(!isBusy);

        busyMessage = message;

        // Forgotten rather than kept, so the next frame writes the label even
        // if the wait is still on the same second it was on.
        shownSeconds = -1;

        if (busyText != null && isBusy)
            busyText.text = string.Format(busyElapsedFormat, message, 0);
    }

    private static void SetDisplayed(VisualElement element, bool displayed)
    {
        if (element != null)
            element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private bool HasSessionService()
    {
        if (sessionService != null)
            return true;

        ShowError("Network session service is missing.");
        return false;
    }

    private void ShowError(string message)
    {
        errorService?.ShowError(message);
    }

    public void HideError()
    {
        errorService?.HideError();
    }
}
