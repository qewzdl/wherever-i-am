using System.Collections.Generic;
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

    // A singleplayer game is a host too, but nobody asked for a LAN session
    // or a lobby, and the wait should not say it is making one.
    [SerializeField] private string singleplayerStartingMessage = "Starting the game...";
    [SerializeField] private string singleplayerStartingDetail = "Nobody else can join this game.";
    [SerializeField] private string singleplayerLoadingMessage = "Loading...";
    [SerializeField] private string joiningDetailFormat = "Host {0}";
    [SerializeField] private string cancellingDetail =
        "Stopping network services safely.";
    [SerializeField] private string busyStepFormat = "STEP {0} / {1}";

    // What is being waited for, and for how long. A connection that is going
    // nowhere looks exactly like one that is about to arrive, and the player
    // deciding whether to press Cancel has nothing else to go on.
    [SerializeField] private string busyElapsedFormat = "{0} s elapsed";

    // What the browser says about itself. An empty list has two meanings that
    // look identical - nobody is hosting, or nothing is listening - and only
    // one of them is a fault the player can do something about.
    // The map page. The confirming button says what it is about to do, which
    // is different for each of the three ways in.
    [Header("Map selection")]
    [SerializeField] private string mapPageFormat = "{0} / {1}";
    [SerializeField] private string startActionText = "Start";
    [SerializeField] private string createLobbyActionText = "Create lobby";
    [SerializeField] private string findLobbiesActionText = "Find lobbies";
    [SerializeField] private string lockedHintFormat = "Win {0} to open this map.";
    [SerializeField] private string lockedJoinHintFormat =
        "Win {0} to host this map. You can still join a game on it.";

    [Header("Server browser")]
    [SerializeField] private string browserListeningText = "Listening";
    [SerializeField] private string browserUnavailableText = "Discovery unavailable";
    [SerializeField] private string browserEmptyText = "No lobbies have answered yet";
    [SerializeField] private string browserEmptyOnMapText = "No lobbies on this map have answered yet";
    [SerializeField] private string showAllMapsText = "All maps";
    [SerializeField] private string onlyMapFormat = "Only {0}";
    [SerializeField] private string browserCountFormat = "{0}/{1}";
    [SerializeField] private string browserFullText = "Full";

    [Header("Join address")]
    // Carries the reason as well as the shape now. The reason used to be a
    // line of its own above the field, where it read as a heading and was
    // indented like a note; under the field it is what the field is for.
    [SerializeField] private string addressHintText =
        "For a lobby this network cannot see. IPv4, for example 192.168.1.10";
    [SerializeField] private string invalidAddressText =
        "Enter a valid IPv4 address.";

    private INetworkSessionService sessionService;
    private INetworkSessionReadService sessionReadService;
    private IUiErrorService errorService;
    private ISettingsScreen settingsScreen;

    private VisualElement boundRoot;
    private VisualElement screen;
    private VisualElement panel;
    private VisualElement joinScreen;
    private VisualElement busyPanel;
    private Label busyText;
    private Label busyStep;
    private Label busyDetail;
    private Label busyElapsed;
    private Label addressHint;
    private TextField playerName;
    private TextField address;
    private ScrollView browser;
    private Label browserStatus;
    private Button refreshButton;

    private LanLobbyDiscovery discovery;
    private readonly List<string> shownLobbies = new();

    // The room picked out of the list, held here rather than written into the
    // address field. Nobody needs to read an address to join a room they can
    // see by name, and a list of everybody's addresses is a list that leaves
    // with every screenshot.
    private string chosenAddress = string.Empty;
    private string chosenName = string.Empty;
    private Button hostButton;

    private enum MapIntent
    {
        Singleplayer,
        Host,
        Join
    }

    // Every map, whatever the browser is showing.
    public const int AnyMap = -1;

    private IGameMapCatalog mapCatalog;
    private const string MapScreenOpenClass = "map-screen--open";
    private const string JoinScreenOpenClass = "join-screen--open";

    private VisualElement mapScreen;
    private VisualElement masthead;
    private VisualElement mapPreview;
    private Label mapName;
    private Label mapDescription;
    private Label mapHint;
    private Label mapPage;
    private Button mapPreviousButton;
    private Button mapNextButton;
    private Button mapConfirmButton;
    private Button mapBackButton;
    private Button mapAllLobbiesButton;
    private Button browserFilterButton;
    private MapIntent mapIntent;
    private int mapIndex;

    // Which map the browser shows rooms for, and the last one chosen, so the
    // filter can be switched off and back on again.
    private int browserMapId = AnyMap;
    private int chosenMapId = AnyMap;
    private Button singleplayerButton;
    private Button multiplayerButton;
    private Button backButton;
    private VisualElement mainButtons;
    private VisualElement multiplayerButtons;

    // Whether the request in flight is a singleplayer game, for what the wait
    // says while it lasts.
    private bool requestIsSingleplayer;
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
        INetworkSessionReadService sessionReadService = null,
        IGameMapCatalog mapCatalog = null)
    {
        UnsubscribeFromSessionState();

        this.sessionService = sessionService;
        this.sessionReadService = sessionReadService;
        this.mapCatalog = mapCatalog;
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
        StopListening();
        EndRequest();
    }

    // Forget everything and ask again. The forgetting is the half that
    // matters: a lobby that has closed would otherwise sit in the list looking
    // joinable until its own timeout ran out, and Refresh is exactly the press
    // of somebody who does not believe what they are looking at.
    private void RefreshLobbies()
    {
        // The room that was picked may not answer this time, and a mark left
        // on a row that is about to be replaced points at whoever takes its
        // place.
        ForgetChosenLobby();

        discovery?.Refresh();
        RefreshBrowser();
    }

    private void OnDestroy()
    {
        UiLocalization.Changed -= HandleLanguageChanged;
        screen?.UnregisterCallback<NavigationCancelEvent>(HandleCancelPressed);
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

        if (IsMapSelectOpen)
        {
            CloseMapSelect();
            evt.StopPropagation();
            return;
        }

        if (IsJoinOpen)
        {
            ReturnToMapSelect();
            evt.StopPropagation();
            return;
        }

        if (IsMultiplayerOpen)
        {
            CloseMultiplayer();
            evt.StopPropagation();
        }
    }

    private bool IsMultiplayerOpen =>
        multiplayerButtons != null &&
        multiplayerButtons.resolvedStyle.display != DisplayStyle.None &&
        multiplayerButtons.style.display != DisplayStyle.None;

    // One list or the other, never both: Multiplayer is a step down into
    // Create and Join, and Back is the step up again.
    private void OpenMultiplayer()
    {
        if (isRequestInFlight)
            return;

        SetMultiplayerOpen(true, moveFocus: true);
        sounds?.Play(UiSoundType.Open);
    }

    private void CloseMultiplayer()
    {
        SetMultiplayerOpen(false, moveFocus: true);
    }

    private void SetMultiplayerOpen(bool open, bool moveFocus)
    {
        if (mainButtons != null)
            mainButtons.style.display = open ? DisplayStyle.None : DisplayStyle.Flex;

        if (multiplayerButtons != null)
            multiplayerButtons.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

        if (!moveFocus)
            return;

        Button target = open ? hostButton : multiplayerButton;
        screen?.schedule.Execute(() => target?.Focus());
    }

    // The clock runs from the moment the request started, not from the last
    // thing that happened to it: cancelling keeps counting, because what the
    // player is waiting on is still the same wait.
    private void Update()
    {
        TickDiscovery();

        if (!isRequestInFlight || busyElapsed == null)
            return;

        int seconds = Mathf.FloorToInt(Time.unscaledTime - requestStartedAt);

        if (seconds == shownSeconds)
            return;

        shownSeconds = seconds;
        busyElapsed.text = string.Format(UiLocalization.Text(busyElapsedFormat), seconds);
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

        // And the language, in the same breath and for the same reason:
        // it belongs to the tree rather than to this screen, and a tree
        // is built whenever Unity feels like building one.
        UiLocalization.Apply(root);
        UiLocalization.Changed -= HandleLanguageChanged;
        UiLocalization.Changed += HandleLanguageChanged;
        screen = root.Q<VisualElement>("Screen");
        panel = root.Q<VisualElement>("Panel");
        joinScreen = root.Q<VisualElement>("JoinScreen");
        busyPanel = root.Q<VisualElement>("BusyPanel");
        busyText = root.Q<Label>("BusyText");
        busyStep = root.Q<Label>("BusyStep");
        busyDetail = root.Q<Label>("BusyDetail");
        busyElapsed = root.Q<Label>("BusyElapsed");
        addressHint = root.Q<Label>("AddressHint");
        playerName = root.Q<TextField>("PlayerName");
        address = root.Q<TextField>("Address");
        browser = root.Q<ScrollView>("Browser");
        browserStatus = root.Q<Label>("BrowserStatus");
        refreshButton = root.Q<Button>("RefreshButton");

        // Both of these are typed into, so both need the same shortcut fixed:
        // without it Ctrl+A empties the box it was meant to light up.
        UiTextInput.Guard(playerName);
        UiTextInput.Guard(address);
        hostButton = root.Q<Button>("HostButton");
        singleplayerButton = root.Q<Button>("SingleplayerButton");
        multiplayerButton = root.Q<Button>("MultiplayerButton");
        backButton = root.Q<Button>("BackButton");
        mainButtons = root.Q<VisualElement>("MainButtons");
        multiplayerButtons = root.Q<VisualElement>("MultiplayerButtons");
        joinButton = root.Q<Button>("JoinButton");
        settingsButton = root.Q<Button>("SettingsButton");
        quitButton = root.Q<Button>("QuitButton");
        connectButton = root.Q<Button>("ConnectButton");
        cancelJoinButton = root.Q<Button>("CancelJoinButton");
        cancelRequestButton = root.Q<Button>("CancelRequestButton");
        mapScreen = root.Q<VisualElement>("MapScreen");
        masthead = root.Q<VisualElement>("Masthead");
        mapPreview = root.Q<VisualElement>("MapPreview");
        mapName = root.Q<Label>("MapName");
        mapDescription = root.Q<Label>("MapDescription");
        mapHint = root.Q<Label>("MapHint");
        mapPage = root.Q<Label>("MapPage");
        mapPreviousButton = root.Q<Button>("MapPreviousButton");
        mapNextButton = root.Q<Button>("MapNextButton");

        mapConfirmButton = root.Q<Button>("MapConfirmButton");
        mapBackButton = root.Q<Button>("MapBackButton");
        mapAllLobbiesButton = root.Q<Button>("MapAllLobbiesButton");
        browserFilterButton = root.Q<Button>("BrowserFilterButton");

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

        Subscribe();

        if (joinScreen != null)
            joinScreen.style.display = DisplayStyle.None;

        if (mapScreen != null)
            mapScreen.style.display = DisplayStyle.None;
        SetMultiplayerOpen(false, moveFocus: false);
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
            hostButton.clicked += ChooseMapToHost;

        if (singleplayerButton != null)
            singleplayerButton.clicked += ChooseMapToPlayAlone;

        if (mapPreviousButton != null)
            mapPreviousButton.clicked += ShowPreviousMap;

        if (mapNextButton != null)
            mapNextButton.clicked += ShowNextMap;

        mapPreviousButton?.RegisterCallback<PointerEnterEvent>(HandleArrowHovered);
        mapNextButton?.RegisterCallback<PointerEnterEvent>(HandleArrowHovered);

        if (mapConfirmButton != null)
            mapConfirmButton.clicked += ConfirmMap;

        if (mapBackButton != null)
            mapBackButton.clicked += CloseMapSelect;

        if (mapAllLobbiesButton != null)
            mapAllLobbiesButton.clicked += ShowEveryLobby;

        if (browserFilterButton != null)
            browserFilterButton.clicked += ToggleBrowserFilter;

        if (multiplayerButton != null)
            multiplayerButton.clicked += OpenMultiplayer;

        if (backButton != null)
            backButton.clicked += CloseMultiplayer;

        if (joinButton != null)
            joinButton.clicked += ChooseMapToJoin;

        if (settingsButton != null)
            settingsButton.clicked += OpenSettings;

        if (quitButton != null)
            quitButton.clicked += Quit;

        if (connectButton != null)
            connectButton.clicked += Join;

        if (cancelJoinButton != null)
            cancelJoinButton.clicked += ReturnToMapSelect;

        if (refreshButton != null)
            refreshButton.clicked += RefreshLobbies;

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
            hostButton.clicked -= ChooseMapToHost;

        if (singleplayerButton != null)
            singleplayerButton.clicked -= ChooseMapToPlayAlone;

        if (mapPreviousButton != null)
            mapPreviousButton.clicked -= ShowPreviousMap;

        if (mapNextButton != null)
            mapNextButton.clicked -= ShowNextMap;

        mapPreviousButton?.UnregisterCallback<PointerEnterEvent>(HandleArrowHovered);
        mapNextButton?.UnregisterCallback<PointerEnterEvent>(HandleArrowHovered);

        if (mapConfirmButton != null)
            mapConfirmButton.clicked -= ConfirmMap;

        if (mapBackButton != null)
            mapBackButton.clicked -= CloseMapSelect;

        if (mapAllLobbiesButton != null)
            mapAllLobbiesButton.clicked -= ShowEveryLobby;

        if (browserFilterButton != null)
            browserFilterButton.clicked -= ToggleBrowserFilter;

        if (multiplayerButton != null)
            multiplayerButton.clicked -= OpenMultiplayer;

        if (backButton != null)
            backButton.clicked -= CloseMultiplayer;

        if (joinButton != null)
            joinButton.clicked -= ChooseMapToJoin;

        if (settingsButton != null)
            settingsButton.clicked -= OpenSettings;

        if (quitButton != null)
            quitButton.clicked -= Quit;

        if (connectButton != null)
            connectButton.clicked -= Join;

        if (cancelJoinButton != null)
            cancelJoinButton.clicked -= ReturnToMapSelect;

        if (refreshButton != null)
            refreshButton.clicked -= RefreshLobbies;

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
        ForgetChosenLobby();
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

    private void ShowJoinPrompt(int mapId)
    {
        if (isRequestInFlight)
            return;

        browserMapId = mapId;

        if (mapId != AnyMap)
            chosenMapId = mapId;

        ForgetChosenLobby();
        RefreshBrowserFilter();

        StartListening();
        RefreshAddressValidation();
        SetScreenOpen(joinScreen, JoinScreenOpenClass, true);
        sounds?.Play(UiSoundType.Open);
        address?.Focus();
    }

    // Opened with the panel and closed with it. A socket held for the whole
    // life of the main menu would be listening to a network nobody is looking
    // at, and this one is only ever read while the list is on screen.
    private void StartListening()
    {
        if (discovery != null)
            return;

        discovery = new LanLobbyDiscovery(LanLobbyNetwork.ProtocolVersion);
        discovery.Changed += RefreshBrowser;

        // Asked out loud straight away, so the list is filled by whoever is
        // already up rather than by whoever beacons next.
        discovery.Refresh();
        RefreshBrowser();
    }

    private void StopListening()
    {
        if (discovery == null)
            return;

        discovery.Changed -= RefreshBrowser;
        discovery.Dispose();
        discovery = null;
    }

    // Only what has already arrived, once a frame, and only while the list is
    // being looked at.
    private void TickDiscovery()
    {
        discovery?.Tick();
    }

    // What the browser says about itself and what the address field is for are
    // both written from here rather than by the markup.
    private void HandleLanguageChanged()
    {
        RefreshBrowser();
        RefreshAddressValidation();
    }

    private void RefreshBrowser()
    {
        if (browser == null)
            return;

        if (browserStatus != null)
        {
            browserStatus.text = discovery != null && discovery.IsListening
                ? UiLocalization.Text(browserListeningText)
                : UiLocalization.Text(browserUnavailableText);
        }

        // A lobby ages off this list when it stops speaking, and until now the
        // choice made on it stayed behind: the row went, the mark went with
        // it, and Join stayed lit over a room that had closed. A room that
        // filled up while it was chosen is let go the same way.
        if (chosenAddress.Length > 0 &&
            (discovery == null ||
             !discovery.Knows(chosenAddress) ||
             !IsJoinable(chosenAddress)))
        {
            ForgetChosenLobby();
        }

        IReadOnlyList<LanLobbyDiscovery.Entry> lobbies =
            discovery != null ? FilterByMap(discovery.Lobbies, browserMapId) : null;

        // Rebuilt only when the rows would actually read differently. The list
        // is redrawn on every beacon otherwise, which is once a second per
        // lobby, and a row rebuilt under the pointer is a row that cannot be
        // clicked.
        if (!HasBrowserChanged(lobbies))
        {
            MarkChosen();
            return;
        }

        shownLobbies.Clear();
        browser.Clear();

        if (lobbies == null || lobbies.Count == 0)
        {
            Label empty = new Label(UiLocalization.Text(
                browserMapId == AnyMap ? browserEmptyText : browserEmptyOnMapText));
            empty.AddToClassList("browser__empty");
            browser.Add(empty);
            return;
        }

        for (int i = 0; i < lobbies.Count; i++)
            browser.Add(BuildLobbyRow(lobbies[i]));

        MarkChosen();
        sounds?.Bind();
    }

    // The rooms playing one map, or all of them.
    public static List<LanLobbyDiscovery.Entry> FilterByMap(
        IReadOnlyList<LanLobbyDiscovery.Entry> lobbies,
        int mapId)
    {
        List<LanLobbyDiscovery.Entry> shown = new();

        if (lobbies == null)
            return shown;

        for (int i = 0; i < lobbies.Count; i++)
        {
            if (mapId == AnyMap || lobbies[i].Advert.mapId == mapId)
                shown.Add(lobbies[i]);
        }

        return shown;
    }

    // On the list and with a seat left.
    private bool IsJoinable(string lobbyAddress)
    {
        if (discovery == null)
            return false;

        foreach (LanLobbyDiscovery.Entry entry in FilterByMap(discovery.Lobbies, browserMapId))
        {
            if (entry.Address == lobbyAddress)
                return !entry.Advert.IsFull;
        }

        return false;
    }

    // One button, two states: narrowed to the map that was chosen, or every
    // room on the network. Hidden when no map was ever chosen - there is
    // nothing to narrow to.
    private void ToggleBrowserFilter()
    {
        browserMapId = browserMapId == AnyMap ? chosenMapId : AnyMap;
        ForgetChosenLobby();
        RefreshBrowserFilter();
        RefreshBrowser();
    }

    private void RefreshBrowserFilter()
    {
        if (browserFilterButton == null)
            return;

        browserFilterButton.style.display = chosenMapId == AnyMap
            ? DisplayStyle.None
            : DisplayStyle.Flex;

        browserFilterButton.text = browserMapId == AnyMap
            ? string.Format(UiLocalization.Text(onlyMapFormat), MapDisplayName(chosenMapId))
            : UiLocalization.Text(showAllMapsText);
    }

    private string MapDisplayName(int mapId)
    {
        return mapCatalog != null && mapCatalog.TryGetMap(mapId, out GameMapDefinition map)
            ? UiLocalization.Text(map.DisplayName)
            : string.Empty;
    }

    // ------------------------------------------------------------ map page

    private bool IsMapSelectOpen =>
        mapScreen != null && mapScreen.ClassListContains(MapScreenOpenClass);

    private bool IsJoinOpen =>
        joinScreen != null && joinScreen.ClassListContains(JoinScreenOpenClass);

    private void SetMapScreenOpen(bool open) => SetScreenOpen(mapScreen, MapScreenOpenClass, open);

    // A screen of its own - the maps, the rooms: the menu and the masthead
    // step aside while it is up, and come back when it goes, over the same
    // picture.
    private void SetScreenOpen(VisualElement layer, string openClass, bool open)
    {
        if (panel != null)
            panel.style.display = open ? DisplayStyle.None : DisplayStyle.Flex;

        if (masthead != null)
            masthead.style.display = open ? DisplayStyle.None : DisplayStyle.Flex;

        UiFade.Set(layer, open, openClass);
    }

    private void ChooseMapToPlayAlone() => OpenMapSelect(MapIntent.Singleplayer);

    private void ChooseMapToHost() => OpenMapSelect(MapIntent.Host);

    private void ChooseMapToJoin() => OpenMapSelect(MapIntent.Join);

    // Always from the first page, in the order the maps are played.
    private void OpenMapSelect(MapIntent intent)
    {
        if (isRequestInFlight)
            return;

        if (mapCatalog == null || mapCatalog.Count == 0)
        {
            ShowError("There are no maps to choose from.");
            return;
        }

        mapIntent = intent;
        mapIndex = 0;
        ShowMapPage();
        SetMapScreenOpen(true);
        sounds?.Play(UiSoundType.Open);
        FocusMapScreen();
    }

    private void FocusMapScreen()
    {
        screen?.schedule.Execute(() => (mapConfirmButton.enabledSelf ? mapConfirmButton : mapBackButton)?.Focus());
    }

    // One step back from the rooms: to the map they were listed for, on the
    // same page, rather than past it to the menu. Both ways in - a map's own
    // rooms and every room - start from the map screen.
    private void ReturnToMapSelect()
    {
        if (!IsJoinOpen || isRequestInFlight)
            return;

        StopListening();
        SetScreenOpen(joinScreen, JoinScreenOpenClass, false);

        ShowMapPage();
        SetMapScreenOpen(true);
        FocusMapScreen();
    }

    private void CloseMapSelect()
    {
        if (!IsMapSelectOpen)
            return;

        SetMapScreenOpen(false);

        Button opener = mapIntent == MapIntent.Singleplayer
            ? singleplayerButton
            : mapIntent == MapIntent.Host ? hostButton : joinButton;

        screen?.schedule.Execute(() => opener?.Focus());
    }

    private void ShowPreviousMap()
    {
        if (mapIndex <= 0)
            return;

        mapIndex--;
        ShowMapPage();
        KeepFocusOnScreen(mapPreviousButton);
        PlayPageTurn();
    }

    private void ShowNextMap()
    {
        if (mapCatalog == null || mapIndex >= mapCatalog.Count - 1)
            return;

        mapIndex++;
        ShowMapPage();
        KeepFocusOnScreen(mapNextButton);
        PlayPageTurn();
    }

    // The arrows are bare pictures, not .button, so the document's own
    // bindings do not hear them: they say it for themselves, like a button.
    private void PlayPageTurn()
    {
        sounds?.Play(UiSoundType.Click);
    }

    private void HandleArrowHovered(PointerEnterEvent evt)
    {
        sounds?.Play(UiSoundType.Hover);
    }

    // Reaching the first or last page takes away the arrow that was just
    // pressed, and with it the keyboard's place on the screen. The arrow
    // pointing back the other way is where it goes instead.
    private void KeepFocusOnScreen(Button pressed)
    {
        if (pressed == null || pressed.style.display != DisplayStyle.None)
            return;

        Button other = pressed == mapPreviousButton ? mapNextButton : mapPreviousButton;
        Button target = other != null && other.style.display != DisplayStyle.None ? other : mapConfirmButton;
        screen?.schedule.Execute(() => target?.Focus());
    }

    private void ShowMapPage()
    {
        GameMapDefinition map = mapCatalog.GetMapAt(mapIndex);
        bool open = map != null && MapProgress.IsUnlocked(mapCatalog, map.MapId);

        if (mapName != null)
            mapName.text = map != null ? UiLocalization.Text(map.DisplayName) : string.Empty;

        if (mapDescription != null)
        {
            // A map with nothing to say leaves no gap where it would have
            // said it: the hint moves up under the name.
            bool described = map != null && !string.IsNullOrWhiteSpace(map.Description);
            mapDescription.text = described ? UiLocalization.Text(map.Description) : string.Empty;
            mapDescription.style.display = described ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (mapPreview != null)
        {
            mapPreview.style.backgroundImage = map != null && map.Preview != null
                ? new StyleBackground(map.Preview)
                : new StyleBackground(StyleKeyword.None);

            mapPreview.EnableInClassList("map-screen__preview--empty", map == null || map.Preview == null);
            mapPreview.EnableInClassList("map-screen__preview--locked", !open);
        }

        if (mapHint != null)
        {
            GameMapDefinition gate = map != null ? MapProgress.GateOf(mapCatalog, map.MapId) : null;

            mapHint.text = open || gate == null
                ? string.Empty
                : string.Format(
                    UiLocalization.Text(mapIntent == MapIntent.Join ? lockedJoinHintFormat : lockedHintFormat),
                    UiLocalization.Text(gate.DisplayName));
        }

        if (mapPage != null)
            mapPage.text = string.Format(UiLocalization.Text(mapPageFormat), mapIndex + 1, mapCatalog.Count);

        // No arrow where there is no page to turn to.
        if (mapPreviousButton != null)
            mapPreviousButton.style.display = mapIndex > 0 ? DisplayStyle.Flex : DisplayStyle.None;

        if (mapNextButton != null)
            mapNextButton.style.display = mapIndex < mapCatalog.Count - 1 ? DisplayStyle.Flex : DisplayStyle.None;

        if (mapConfirmButton != null)
        {
            mapConfirmButton.text = UiLocalization.Text(mapIntent switch
            {
                MapIntent.Singleplayer => startActionText,
                MapIntent.Host => createLobbyActionText,
                _ => findLobbiesActionText
            });

            // A locked map can be looked for, to join somebody who has it
            // open, but not hosted.
            mapConfirmButton.SetEnabled(map != null && (open || mapIntent == MapIntent.Join));
        }

        if (mapAllLobbiesButton != null)
        {
            mapAllLobbiesButton.style.display = mapIntent == MapIntent.Join
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }
    }

    private void ConfirmMap()
    {
        GameMapDefinition map = mapCatalog?.GetMapAt(mapIndex);

        if (map == null)
            return;

        SetMapScreenOpen(false);

        switch (mapIntent)
        {
            case MapIntent.Singleplayer:
                Singleplayer(map.MapId);
                break;
            case MapIntent.Host:
                Host(map.MapId);
                break;
            default:
                ShowJoinPrompt(map.MapId);
                break;
        }
    }

    private void ShowEveryLobby()
    {
        SetMapScreenOpen(false);
        ShowJoinPrompt(AnyMap);
    }

    private bool HasBrowserChanged(IReadOnlyList<LanLobbyDiscovery.Entry> lobbies)
    {
        // Nothing drawn yet is a change too: an empty list compared with the
        // empty list it has not shown yet looked the same, and the line that
        // says nobody has answered was never written.
        if (browser.childCount == 0)
            return true;

        int count = lobbies != null ? lobbies.Count : 0;

        if (count != shownLobbies.Count)
            return true;

        for (int i = 0; i < count; i++)
        {
            if (shownLobbies[i] != DescribeLobby(lobbies[i]))
                return true;
        }

        return false;
    }

    // Everything a row shows, in one string. Comparing what is drawn is the
    // only comparison that decides whether it has to be drawn again.
    private string DescribeLobby(LanLobbyDiscovery.Entry entry)
    {
        return string.Concat(
            entry.Address,
            "|",
            entry.Advert.name,
            "|",
            entry.Advert.players.ToString(),
            "|",
            entry.Advert.maxPlayers.ToString());
    }

    private VisualElement BuildLobbyRow(LanLobbyDiscovery.Entry entry)
    {
        shownLobbies.Add(DescribeLobby(entry));

        string lobbyAddress = entry.Address;
        string lobbyName = entry.Advert.name;

        Button row = new Button(() => ChooseLobby(lobbyAddress, lobbyName));
        row.AddToClassList("button");
        row.AddToClassList("browser__row");
        row.EnableInClassList("browser__row--full", entry.Advert.IsFull);

        // Shown, so it is known why nobody is getting in, but not choosable:
        // the server would only turn the connection away.
        row.SetEnabled(!entry.Advert.IsFull);

        Label name = new Label(entry.Advert.name) { enableRichText = false };
        name.AddToClassList("browser__name");
        row.Add(name);

        Label count = new Label(entry.Advert.IsFull
            ? UiLocalization.Text(browserFullText)
            : string.Format(browserCountFormat, entry.Advert.players, entry.Advert.maxPlayers));

        count.AddToClassList("browser__count");
        row.Add(count);

        return row;
    }

    // Picking a row marks it and nothing else. It does not connect - a list
    // where one click leaves the menu is a list nobody dares click - and it
    // does not write the address anywhere it can be read.
    private void ChooseLobby(string lobbyAddress, string lobbyName)
    {
        chosenAddress = lobbyAddress;
        chosenName = lobbyName;

        RefreshAddressValidation();
        MarkChosen();
    }

    // Typing is the other way in, and the two cannot both be the answer. The
    // last thing the player did wins, which is the only rule that needs no
    // explaining.
    private void ForgetChosenLobby()
    {
        if (chosenAddress.Length == 0)
            return;

        chosenAddress = string.Empty;
        chosenName = string.Empty;
        MarkChosen();

        // The chosen room is half of what enables Join - the address box is
        // the other half - so dropping it has to ask that question again.
        // Every caller used to have to remember this, and Refresh did not:
        // it forgot the room and left the button lit over an empty box.
        RefreshAddressValidation();
    }

    private void MarkChosen()
    {
        if (browser?.contentContainer == null)
            return;

        for (int i = 0; i < browser.contentContainer.childCount; i++)
        {
            VisualElement row = browser.contentContainer[i];

            row.EnableInClassList(
                "browser__row--chosen",
                chosenAddress.Length > 0 &&
                i < shownLobbies.Count &&
                shownLobbies[i].StartsWith(
                    chosenAddress + "|",
                    System.StringComparison.Ordinal));
        }
    }

    private void HideJoinPrompt()
    {
        bool wasOpen = IsJoinOpen;

        StopListening();

        if (wasOpen)
            SetScreenOpen(joinScreen, JoinScreenOpenClass, false);

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

        // A room picked from the list is as good an answer as a typed address,
        // and a better one: nobody can mistype it.
        connectButton?.SetEnabled(
            (isValid || chosenAddress.Length > 0) && !isRequestInFlight);
        address.EnableInClassList(InvalidInputClass, showError);

        if (addressHint == null)
            return;

        addressHint.text = showError ? UiLocalization.Text(invalidAddressText) : UiLocalization.Text(addressHintText);
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
    // A game for one. Still a host underneath - see SessionMode - but one
    // nobody else can reach or find.
    public async void Singleplayer(int? mapId = null)
    {
        SavePlayerName();

        if (!TryBeginRequest(UiLocalization.Text(singleplayerStartingDetail)))
            return;

        requestIsSingleplayer = true;

        try
        {
            if (!HasSessionService())
                return;

            await sessionService.HostSingleplayerAsync(mapId);
        }
        finally
        {
            CompleteRequestInvocation();
        }
    }

    public async void Host(int? mapId = null)
    {
        SavePlayerName();

        if (!TryBeginRequest(UiLocalization.Text(hostingDetail)))
            return;

        try
        {
            if (!HasSessionService())
                return;

            await sessionService.HostLanAsync(mapId);
        }
        finally
        {
            CompleteRequestInvocation();
        }
    }

    // Two ways in, and the room picked from the list wins while it is picked.
    // Only the typed one is saved: the address box remembers what a player
    // typed, and a room they clicked once is not something they asked to keep.
    public void Join()
    {
        if (chosenAddress.Length > 0)
        {
            Join(chosenAddress, chosenName);
            return;
        }

        SaveJoinAddress();
        Join(address != null ? address.value : string.Empty);
    }

    public void Join(string host)
    {
        Join(host, string.Empty);
    }

    // The name is what the progress line says when there is one. A player who
    // picked a room called Alex is waiting for Alex, not for a number they
    // never read - and a number they never read is a number this screen has no
    // business putting up in front of them.
    public async void Join(string host, string lobbyName)
    {
        SavePlayerName();

        // Normalised here as well as on the way to storage: Join is public and
        // whatever reaches the session service should be what was stored.
        host = JoinAddressProvider.Normalize(host);

        string describedAs = string.IsNullOrWhiteSpace(lobbyName) ? host : lobbyName;

        if (!TryBeginRequest(string.Format(UiLocalization.Text(joiningDetailFormat), describedAs)))
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
        SetBusy(true, UiLocalization.Text(cancellingMessage), string.Empty, UiLocalization.Text(cancellingDetail));
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
            UiLocalization.Text(preparingMessage),
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
        requestIsSingleplayer = false;
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
            busyElapsed.text = string.Format(UiLocalization.Text(busyElapsedFormat), 0);
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
                    UiLocalization.Text(requestIsSingleplayer ? singleplayerStartingMessage : hostingMessage),
                    FormatStep(1, 2),
                    requestDetail);
                break;

            case NetworkSessionState.StartingClient:
                SetBusy(
                    true,
                    UiLocalization.Text(joiningMessage),
                    FormatStep(1, 2),
                    requestDetail);
                break;

            case NetworkSessionState.LoadingLobby:
                SetBusy(
                    true,
                    UiLocalization.Text(requestIsSingleplayer ? singleplayerLoadingMessage : loadingLobbyMessage),
                    FormatStep(2, 2),
                    requestDetail);
                break;

            case NetworkSessionState.Lobby:
                SetBusy(
                    true,
                    UiLocalization.Text(requestIsSingleplayer ? singleplayerLoadingMessage : openingLobbyMessage),
                    FormatStep(2, 2),
                    requestDetail);
                break;

            case NetworkSessionState.LoadingGame:
            case NetworkSessionState.InGame:
                SetBusy(
                    true,
                    UiLocalization.Text(loadingGameMessage),
                    FormatStep(2, 2),
                    requestDetail);
                break;

            case NetworkSessionState.Disconnecting:
                SetBusy(
                    true,
                    UiLocalization.Text(cancellingMessage),
                    string.Empty,
                    UiLocalization.Text(cancellingDetail));
                break;
        }
    }

    private string FormatStep(int current, int total)
    {
        return string.Format(UiLocalization.Text(busyStepFormat), current, total);
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
