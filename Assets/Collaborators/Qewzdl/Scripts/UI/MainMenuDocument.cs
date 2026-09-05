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
    private const string OverlayOpenClass = "overlay--open";
    private const string InvalidInputClass = "input--invalid";
    private const string InputErrorClass = "input__hint--error";
    private const int NameLengthLimit = 16;

    [Header("References")]
    [SerializeField] private UIDocument document;
    [SerializeField] private UiDocumentSounds sounds;

    [Header("While connecting")]
    [SerializeField] private string preparingMessage = "Preparing network...";
    [SerializeField] private string hostingMessage = "Starting LAN host...";
    [SerializeField] private string joiningMessage =
        "Contacting host and awaiting approval...";
    [SerializeField] private string loadingLobbyMessage = "Loading lobby...";
    [SerializeField] private string openingLobbyMessage = "Opening lobby...";
    [SerializeField] private string loadingGameMessage = "Joining match...";
    [SerializeField] private string cancellingMessage = "Cancelling connection...";
    [SerializeField] private string hostingDetail =
        "This device will host the LAN session.";
    [SerializeField] private string joiningDetailFormat = "Host {0}";
    [SerializeField] private string cancellingDetail =
        "Stopping network services safely.";
    [SerializeField] private string busyStepFormat = "STEP {0} / {1}";

    // What is being waited for, and for how long. A connection that is going
    // nowhere looks exactly like one that is about to arrive, and the player
    // deciding whether to press Cancel has nothing else to go on.
    [SerializeField] private string busyElapsedFormat = "{0} s elapsed";

    [Header("Join address")]
    [SerializeField] private string addressHintText =
        "IPv4 address, for example 192.168.1.10";
    [SerializeField] private string invalidAddressText =
        "Enter a valid IPv4 address.";

    private INetworkSessionService sessionService;
    private INetworkSessionReadService sessionReadService;
    private IUiErrorService errorService;
    private ISettingsScreen settingsScreen;

    private VisualElement boundRoot;
    private VisualElement screen;
    private VisualElement panel;
    private VisualElement joinPanel;
    private VisualElement busyPanel;
    private Label busyText;
    private Label busyStep;
    private Label busyDetail;
    private Label busyElapsed;
    private Label addressHint;
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

    private string requestDetail = string.Empty;
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
        ISettingsScreen settingsScreen,
        INetworkSessionReadService sessionReadService = null)
    {
        UnsubscribeFromSessionState();

        this.sessionService = sessionService;
        this.sessionReadService = sessionReadService;
        this.errorService = errorService;
        this.settingsScreen = settingsScreen;

        SubscribeToSessionState();
        Show(complainIfMissing: false);
    }

    public void Dispose()
    {
        UnsubscribeFromSessionState();
        sessionService = null;
        sessionReadService = null;
        errorService = null;
        settingsScreen = null;
        EndRequest();
    }

    private void OnDestroy()
    {
        screen?.UnregisterCallback<NavigationCancelEvent>(HandleCancelPressed);
        joinPanel?.UnregisterCallback<ClickEvent>(HandleJoinBackdropClicked);
        Unsubscribe();
        Dispose();
    }

    // Backwards out of whatever is on top. A request in flight is cancelled
    // first because it is the thing standing over everything else; the address
    // prompt is next; and the menu itself has nowhere to go back to, so Escape
    // there does nothing rather than quitting the game.
    private void HandleCancelPressed(NavigationCancelEvent evt)
    {
        if (isRequestInFlight)
        {
            if (!isCancelling)
                CancelRequest();

            evt.StopPropagation();
            return;
        }

        if (joinPanel != null && joinPanel.style.display == DisplayStyle.Flex)
        {
            HideJoinPrompt();
            evt.StopPropagation();
        }
    }

    // The clock runs from the moment the request started, not from the last
    // thing that happened to it: cancelling keeps counting, because what the
    // player is waiting on is still the same wait.
    private void Update()
    {
        if (!isRequestInFlight || busyElapsed == null)
            return;

        int seconds = Mathf.FloorToInt(Time.unscaledTime - requestStartedAt);

        if (seconds == shownSeconds)
            return;

        shownSeconds = seconds;
        busyElapsed.text = string.Format(busyElapsedFormat, seconds);
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

        // Whatever the player set for the interface as a whole - its scale, its
        // text size, whether it moves - applies to this tree too, and applies
        // now rather than the next time they open the settings screen.
        UiPreferences.Attach(root);
        screen = root.Q<VisualElement>("Screen");
        panel = root.Q<VisualElement>("Panel");
        joinPanel = root.Q<VisualElement>("JoinPanel");
        busyPanel = root.Q<VisualElement>("BusyPanel");
        busyText = root.Q<Label>("BusyText");
        busyStep = root.Q<Label>("BusyStep");
        busyDetail = root.Q<Label>("BusyDetail");
        busyElapsed = root.Q<Label>("BusyElapsed");
        addressHint = root.Q<Label>("AddressHint");
        playerName = root.Q<TextField>("PlayerName");
        address = root.Q<TextField>("Address");

        // Both of these are typed into, so both need the same shortcut fixed:
        // without it Ctrl+A empties the box it was meant to light up.
        UiTextInput.Guard(playerName);
        UiTextInput.Guard(address);
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

        // A text field selects everything it holds the moment it is touched,
        // which is right for a field you are about to replace and wrong for
        // one that already says what you wanted. Both of these arrive filled
        // in - the name and the address are remembered between runs - so a
        // click is far more likely to mean "fix a character" than "start over".
        root.Query<TextField>().ForEach(field =>
        {
            field.selectAllOnFocus = false;
            field.selectAllOnMouseUp = false;
        });

        // Escape, and the cancel button on a pad - one event covers both. It
        // reaches here by bubbling out of whatever is focused inside the
        // screen, which is why every panel below hands focus to something when
        // it opens.
        screen.RegisterCallback<NavigationCancelEvent>(HandleCancelPressed);
        joinPanel?.RegisterCallback<ClickEvent>(HandleJoinBackdropClicked);

        Subscribe();
        SetDisplayed(joinPanel, false);
        SetBusy(false, string.Empty, string.Empty, string.Empty);

        if (playerName != null)
        {
            playerName.maxLength = NameLengthLimit;
            playerName.SetValueWithoutNotify(PlayerNameProvider.Get());
        }

        address?.SetValueWithoutNotify(JoinAddressProvider.Get());
        RefreshAddressValidation();

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
        address?.RegisterValueChangedCallback(HandleAddressChanged);
        address?.RegisterCallback<KeyDownEvent>(HandleAddressKeyDown);
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
        address?.UnregisterValueChangedCallback(HandleAddressChanged);
        address?.UnregisterCallback<KeyDownEvent>(HandleAddressKeyDown);
    }

    private void HandleNameCommitted(FocusOutEvent evt)
    {
        SavePlayerName();
    }

    private void HandleAddressCommitted(FocusOutEvent evt)
    {
        SaveJoinAddress();
    }

    private void HandleAddressChanged(ChangeEvent<string> evt)
    {
        RefreshAddressValidation();
    }

    private void HandleAddressKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
            return;

        if (connectButton == null || !connectButton.enabledSelf)
            return;

        evt.StopPropagation();
        Join();
    }

    private void HandleJoinBackdropClicked(ClickEvent evt)
    {
        if (!ReferenceEquals(evt.target, joinPanel) || isRequestInFlight)
            return;

        HideJoinPrompt();
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

        RefreshAddressValidation();
        SetDisplayed(joinPanel, true);
        sounds?.Play(UiSoundType.Open);
        address?.Focus();
    }

    private void HideJoinPrompt()
    {
        bool wasOpen = joinPanel != null &&
                       joinPanel.style.display == DisplayStyle.Flex;

        SetDisplayed(joinPanel, false);

        if (wasOpen && !isRequestInFlight && joinButton != null)
            screen?.schedule.Execute(() => joinButton.Focus());
    }

    private void RefreshAddressValidation()
    {
        if (address == null)
            return;

        string value = address.value;
        bool isEmpty = string.IsNullOrWhiteSpace(value);
        bool isValid = LanAddressValidator.TryNormalize(value, out _);
        bool showError = !isEmpty && !isValid;

        connectButton?.SetEnabled(isValid && !isRequestInFlight);
        address.EnableInClassList(InvalidInputClass, showError);

        if (addressHint == null)
            return;

        addressHint.text = showError ? invalidAddressText : addressHintText;
        addressHint.EnableInClassList(InputErrorClass, showError);
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

        if (!TryBeginRequest(hostingDetail))
            return;

        try
        {
            if (!HasSessionService())
                return;

            await sessionService.HostLanAsync();
        }
        finally
        {
            CompleteRequestInvocation();
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

        if (!TryBeginRequest(string.Format(joiningDetailFormat, host)))
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
            CompleteRequestInvocation();
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
        SetBusy(true, cancellingMessage, string.Empty, cancellingDetail);
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
    private bool TryBeginRequest(string detail)
    {
        if (isRequestInFlight)
            return false;

        isRequestInFlight = true;
        isCancelling = false;
        requestStartedAt = Time.unscaledTime;
        requestDetail = detail;
        HideError();
        SetBusy(
            true,
            preparingMessage,
            FormatStep(current: 1, total: 2),
            requestDetail);
        return true;
    }

    private void CompleteRequestInvocation()
    {
        if (sessionReadService != null &&
            KeepsBusyOverlayOpen(sessionReadService.CurrentState))
        {
            return;
        }

        EndRequest();
    }

    private void EndRequest()
    {
        isRequestInFlight = false;
        requestDetail = string.Empty;
        SetBusy(false, string.Empty, string.Empty, string.Empty);
    }

    // The whole panel is switched off rather than a named list of controls:
    // in uGUI that list had to be kept by hand, and anything left out of it
    // stayed clickable while the menu was busy.
    private void SetBusy(
        bool isBusy,
        string message,
        string step,
        string detail)
    {
        SetDisplayed(busyPanel, isBusy);
        panel?.SetEnabled(!isBusy);

        // Somewhere for the keyboard to be. Cancel is the only thing that can
        // be done while a request is in flight, and a screen with nothing
        // focused answers no key at all.
        if (isBusy && cancelRequestButton != null)
            cancelRequestButton.schedule.Execute(() => cancelRequestButton.Focus());

        // Forgotten rather than kept, so the next frame writes the label even
        // if the wait is still on the same second it was on.
        shownSeconds = -1;

        if (!isBusy)
            return;

        if (busyText != null)
            busyText.text = message;

        if (busyStep != null)
            busyStep.text = step;

        if (busyDetail != null)
            busyDetail.text = detail;

        if (busyElapsed != null)
            busyElapsed.text = string.Format(busyElapsedFormat, 0);
    }

    private void SubscribeToSessionState()
    {
        if (sessionReadService != null)
            sessionReadService.StateChanged += HandleSessionStateChanged;
    }

    private void UnsubscribeFromSessionState()
    {
        if (sessionReadService != null)
            sessionReadService.StateChanged -= HandleSessionStateChanged;
    }

    private void HandleSessionStateChanged(
        NetworkSessionState previous,
        NetworkSessionState current)
    {
        if (!isRequestInFlight)
            return;

        if (!KeepsBusyOverlayOpen(current))
        {
            EndRequest();
            return;
        }

        switch (current)
        {
            case NetworkSessionState.StartingHost:
                SetBusy(
                    true,
                    hostingMessage,
                    FormatStep(1, 2),
                    requestDetail);
                break;

            case NetworkSessionState.StartingClient:
                SetBusy(
                    true,
                    joiningMessage,
                    FormatStep(1, 2),
                    requestDetail);
                break;

            case NetworkSessionState.LoadingLobby:
                SetBusy(
                    true,
                    loadingLobbyMessage,
                    FormatStep(2, 2),
                    requestDetail);
                break;

            case NetworkSessionState.Lobby:
                SetBusy(
                    true,
                    openingLobbyMessage,
                    FormatStep(2, 2),
                    requestDetail);
                break;

            case NetworkSessionState.LoadingGame:
            case NetworkSessionState.InGame:
                SetBusy(
                    true,
                    loadingGameMessage,
                    FormatStep(2, 2),
                    requestDetail);
                break;

            case NetworkSessionState.Disconnecting:
                SetBusy(
                    true,
                    cancellingMessage,
                    string.Empty,
                    cancellingDetail);
                break;
        }
    }

    private string FormatStep(int current, int total)
    {
        return string.Format(busyStepFormat, current, total);
    }

    private static bool KeepsBusyOverlayOpen(NetworkSessionState state)
    {
        return state == NetworkSessionState.StartingHost ||
               state == NetworkSessionState.StartingClient ||
               state == NetworkSessionState.LoadingLobby ||
               state == NetworkSessionState.Lobby ||
               state == NetworkSessionState.LoadingGame ||
               state == NetworkSessionState.InGame ||
               state == NetworkSessionState.Disconnecting;
    }

    private static void SetDisplayed(VisualElement element, bool displayed)
    {
        UiFade.Set(element, displayed, OverlayOpenClass);
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
