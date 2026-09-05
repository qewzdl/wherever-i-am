using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// The lobby, in UI Toolkit.
//
// Same contract as the uGUI one it replaces, down to the six events: the scene
// feature constructs it with the read service, and LobbyUICommandPresenter
// turns what happens here into commands. Nothing above this file changed.
//
// What changed is where the view comes from. Twelve object references wired by
// hand in the scene became one document, the player row prefab became four
// elements built in code, and the screen now reads from the same tokens as
// every other one - which is the actual point. The lobby is the second thing a
// player sees, and it was the first thing that looked like another game.
[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public class LobbyUI : MonoBehaviour
{
    private const string OpenClass = "screen--open";
    private const string OverlayOpenClass = "overlay--open";
    private const string ReadyCompleteClass = "roster__ready-count--complete";
    private const string ConfirmToneClass = "button--confirm";
    private const string DoorOpenClass = "door__status--open";

    [Header("References")]
    [SerializeField] private UIDocument document;
    [SerializeField] private UiDocumentSounds sounds;
    [SerializeField] private EnemyDifficultyCatalog difficultyCatalog;
    [SerializeField] private Texture2D copyAddressIcon;
    [SerializeField] private Texture2D copiedAddressIcon;

    [Header("Text")]
    // Both are verbs. The old pair said "Ready" and "Not ready", and the second
    // of those reads as a state rather than as the thing pressing it does -
    // which is the one question a button label exists to answer.
    [SerializeField] private string startActionText = "Start";
    [SerializeField] private string readyActionText = "Ready up";
    [SerializeField] private string standDownActionText = "Stand down";
    [SerializeField] private string playerCountFormat = "{0}/{1}";
    [SerializeField] private string readyCountFormat = "{0} READY";
    [SerializeField] private string kickConfirmFormat =
        "Remove {0}? They will not be able to join again this session.";
    [SerializeField] private string needMorePlayersFormat = "Need {0} more to start";

    // Who the room is waiting for, said to the person reading it. "Waiting for
    // 1 to get ready" was true for everybody and about nobody: the player
    // holding the room up read it as somebody else's problem, and the four
    // people who were ready had no idea which of them was being counted.
    [SerializeField] private string readyUpPromptText = "Ready up when you're prepared";
    [SerializeField] private string waitingForPlayerFormat = "Waiting for {0}";
    [SerializeField] private string waitingForPlayersFormat = "Waiting for {0} players";
    [SerializeField] private string startingText = "Starting the match...";
    [SerializeField] private string ownerOnlySettingText = "Only the host can change this";

    // The terms of the match, said on the wall of the room. Difficulty only:
    // map and mode get a line each the day either of them has more than one
    // value, and a line naming the only map there is would be furniture.
    [SerializeField] private string matchDifficultyFormat = "Difficulty: {0}";
    [SerializeField] private string settingsOwnerFormat = "{0} sets the terms";
    [SerializeField] private string settingsYoursText = "You set the terms";
    [SerializeField] private string settingsOwnerUnknownText = "The host sets the terms";

    // Readiness is cleared by the server whenever the terms move, and until now
    // the screen said nothing - everybody simply stopped being ready and the
    // only explanation was a dropdown somebody else had touched.
    [SerializeField] private string difficultyChangedByHostFormat =
        "Host changed difficulty to {0} - readiness was reset";
    [SerializeField] private string difficultyChangedByYouFormat =
        "You changed difficulty to {0} - readiness was reset";
    [SerializeField] private float setupNoticeSeconds = 8f;

    // Asked before the change rather than explained after it, because the host
    // is the one person who can still not do it.
    [SerializeField] private string difficultyChangeConfirmFormat =
        "Change difficulty to {0}? Everybody who is ready goes back to not ready.";
    [SerializeField] private string difficultyChangeActionText = "Change";
    // The status line is the state; the hint is what to do about it. They are
    // two lines because the first is read at a glance from across the room and
    // the second is read once, by the host, while they work out what to send.
    [SerializeField] private string doorShutStatusText = "Lobby closed";
    [SerializeField] private string doorOpenStatusText = "Lobby open";
    [SerializeField] private string doorShutText =
        "Nobody can reach this lobby until you open it.";
    [SerializeField] private string doorOpenFormat =
        "Anybody who has {0} can join.";
    [SerializeField] private string doorOpenNoAddressText =
        "Open, but this machine has no network address to hand out.";
    [SerializeField] private string doorNotYoursText = "Only the host can open the door.";
    [SerializeField] private string addressUnknownText = "no network";
    [SerializeField] private string addressCopyTooltip = "Copy invite address";
    [SerializeField] private string addressCopiedText = "Address copied";
    [SerializeField] private string doorFullFormat =
        "Open, but the room is full at {0}. Nobody else can get in.";
    [SerializeField] private string readyToStartText = "Everyone is ready - start the match";
    [SerializeField] private string waitingForHostText = "Waiting for the host to start";

    [Header("Match transition")]
    [SerializeField] private string preparingMatchStageText = "PREPARING";
    [SerializeField] private string loadingMatchStageText = "LOADING SCENE";
    [SerializeField] private string enteringMatchStageText = "ENTERING MATCH";
    [SerializeField] private string leavingMatchStageText = "LEAVING SESSION";
    [SerializeField] private string preparingMatchText = "Starting the match...";
    [SerializeField] private string loadingMatchText = "Loading the selected map...";
    [SerializeField] private string enteringMatchText = "Match is ready...";
    [SerializeField] private string leavingMatchText = "Returning to the menu...";
    [SerializeField] private string hostTransitionDetail =
        "Synchronizing the scene with every player.";
    [SerializeField] private string clientTransitionDetail =
        "The host started the match. Waiting for scene synchronization.";
    [SerializeField] private string leavingTransitionDetail =
        "Stopping network services safely.";
    [SerializeField] private string transitionElapsedFormat = "{0} s elapsed";

    [Header("Player rows")]
    [SerializeField] private string ownerStatusText = "Owner";
    [SerializeField] private string readyStatusText = "Ready";
    [SerializeField] private string notReadyStatusText = "Not ready";
    [SerializeField] private string kickActionText = "Remove";
    [SerializeField] private string localPlayerSuffix = "  (you)";

    // Leaving means two different things depending on who presses it, and the
    // button used to say the smaller one to both.
    [SerializeField] private string leaveActionText = "Leave";
    [SerializeField] private string closeLobbyActionText = "Close lobby";
    [SerializeField] private string closeLobbyConfirmText =
        "Close the lobby? Everybody else in it goes back to the menu.";
    [SerializeField] private string closeLobbyConfirmAloneText =
        "Close the lobby and go back to the menu?";
    [SerializeField] private string closeLobbyActionConfirmText = "Close";
    [SerializeField] private string emptyRosterText = "Waiting for the room...";

    // Ready, the door and the difficulty are all asked of the server and none
    // of them is true until it says so. Until now the controls sat there
    // looking answered - worse, the next refresh put them back where they
    // started, so a host who opened the door watched the switch flick itself
    // shut and then open again when the reply landed.
    [SerializeField] private string pendingChangeText = "Updating...";
    [SerializeField] private string startPendingActionText = "Starting...";
    [SerializeField] private string pendingFailedText = "No answer - try again";
    [SerializeField, Min(1f)] private float pendingChangeTimeoutSeconds = 6f;

    private ILobbyReadService readService;
    private INetworkSessionReadService sessionReadService;
    private int[] difficultyIds = Array.Empty<int>();
    private string[] difficultyDescriptions = Array.Empty<string>();

    private VisualElement boundRoot;
    private VisualElement screen;
    private VisualElement panel;
    private VisualElement roster;
    private VisualElement confirmPanel;
    private VisualElement matchTransitionPanel;
    private VisualElement roomSettingsPanel;
    private Label playerCountLabel;
    private Label readyCountLabel;
    private Label difficultyDescriptionLabel;
    private Label difficultyOwnerNoteLabel;
    private Label doorHintLabel;
    private Label doorStatusLabel;
    private Label setupDifficultyLabel;
    private Label setupOwnerLabel;
    private Label setupNoticeLabel;
    private Label addressLabel;
    private Image addressCopyIcon;

    private VisualElement addressField;
    private Label startHintLabel;
    private Label confirmTextLabel;
    private Label matchTransitionStageLabel;
    private Label matchTransitionTextLabel;
    private Label matchTransitionDetailLabel;
    private Label matchTransitionElapsedLabel;
    private DropdownField difficultyField;
    private DropdownField addressPickField;
    private Toggle visibilityToggle;
    private Button readyButton;
    private Button startButton;
    private Button leaveButton;
    private Button confirmButton;
    private Button confirmCancelButton;
    private Button addressButton;
    private Button transitionLeaveButton;
    private Button roomSettingsButton;
    private Button roomSettingsCloseButton;

    private bool isRoomSettingsOpen;
    private bool isMatchTransitionVisible;
    private float matchTransitionStartedAt;
    private int shownTransitionSeconds = -1;
    private bool isAddressCopyFeedbackVisible;
    private int addressCopyFeedbackVersion;
    private IReadOnlyList<LanAddressProvider.Option> addressOptions =
        Array.Empty<LanAddressProvider.Option>();

    // Two things on this screen are worth asking about twice, and they are the
    // two that cannot be taken back: removing somebody for the rest of the
    // session, and closing a room three other people are standing in. One
    // dialog serves both - it is the same question with a different noun, and
    // a second overlay would have been the same markup twice.
    private enum PendingAction
    {
        None,
        Kick,
        CloseLobby,
        ChangeDifficulty
    }

    // A command sent to the server and not yet answered.
    //
    // There is nothing to listen to: the room's state is replicated, and a
    // refused command is refused in silence - the server checks who is asking
    // and returns. So the wait ends one of two ways. Either the state arrives
    // saying what was asked for, which is the only proof there is, or the
    // clock runs out and the player is told nothing came back.
    private sealed class PendingChange
    {
        public bool IsWaiting;
        public bool HasFailed;

        private float expiresAt;
        private Func<bool> isSettled;

        public void Begin(Func<bool> settled, float timeoutSeconds)
        {
            isSettled = settled;
            IsWaiting = true;
            HasFailed = false;
            expiresAt = Time.unscaledTime + timeoutSeconds;
        }

        // Asked twice: whenever the room says something, in case this is the
        // answer, and once a frame, in case nothing is ever going to say it.
        public void Tick()
        {
            if (!IsWaiting)
                return;

            if (isSettled == null || isSettled())
            {
                IsWaiting = false;
                HasFailed = false;
                return;
            }

            if (Time.unscaledTime < expiresAt)
                return;

            IsWaiting = false;
            HasFailed = true;
        }

        public void Forget()
        {
            IsWaiting = false;
            HasFailed = false;
            isSettled = null;
        }
    }

    private PendingAction pendingAction;
    private ulong pendingKickClientId;
    private int pendingDifficultyId;
    private Button pendingFocusTarget;

    // What the room said last time this screen looked. It is the only way a
    // change is noticed at all: the server does not announce one, the new value
    // simply arrives.
    private int lastSeenDifficultyId;
    private bool hasSeenSettings;

    // One per control, because they are three separate questions and a player
    // may be waiting on their own Ready while the host is waiting on the door.
    private readonly PendingChange readyChange = new PendingChange();
    private readonly PendingChange doorChange = new PendingChange();
    private readonly PendingChange difficultyChange = new PendingChange();

    // Start is the one command whose answer is a phase rather than a value:
    // the room stops being a lobby. It waits the same way regardless.
    private readonly PendingChange startChange = new PendingChange();
    private int setupNoticeVersion;

    public event Action ReadyClicked;
    public event Action StartGameClicked;
    public event Action LeaveLobbyClicked;
    public event Action<int> DifficultySelected;
    public event Action LobbyVisibilityToggleClicked;
    public event Action<ulong> PlayerKickRequested;

    public void Construct(
        ILobbyReadService readService,
        INetworkSessionReadService sessionReadService = null)
    {
        if (this.readService != null)
            this.readService.LobbyChanged -= Refresh;

        UnsubscribeFromSessionState();

        this.readService = readService;
        this.sessionReadService = sessionReadService;

        if (this.readService != null)
            this.readService.LobbyChanged += Refresh;

        SubscribeToSessionState();

        hasSeenSettings = false;
        readyChange.Forget();
        doorChange.Forget();
        difficultyChange.Forget();
        startChange.Forget();
        HideSetupNotice(++setupNoticeVersion);

        Show(complainIfMissing: false);
        Refresh();
    }

    public void Dispose()
    {
        if (readService != null)
            readService.LobbyChanged -= Refresh;

        UnsubscribeFromSessionState();
        readService = null;
        sessionReadService = null;
        hasSeenSettings = false;
        isAddressCopyFeedbackVisible = false;
        addressCopyFeedbackVersion++;
        SetMatchTransitionVisible(false);
    }

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
        Refresh();
    }

    // By Start the scene has finished waking up, so a document with nothing in
    // it is a fault worth saying out loud.
    private void Start()
    {
        Show(complainIfMissing: true);
        Refresh();
    }

    private void Update()
    {
        TickPendingChanges();

        if (!isMatchTransitionVisible || matchTransitionElapsedLabel == null)
            return;

        int seconds = Mathf.FloorToInt(
            Time.unscaledTime - matchTransitionStartedAt);

        if (seconds == shownTransitionSeconds)
            return;

        shownTransitionSeconds = seconds;
        matchTransitionElapsedLabel.text = string.Format(
            transitionElapsedFormat,
            seconds);
    }

    // A wait that ends by running out has nothing to announce it: the room
    // will not change, so Refresh will not be called, so the clock has to be
    // read here. Only the frames where something actually expires cost
    // anything - the rest is three boolean checks.
    private void TickPendingChanges()
    {
        if (readService == null || boundRoot == null)
            return;

        bool wasWaiting = readyChange.IsWaiting ||
                          doorChange.IsWaiting ||
                          difficultyChange.IsWaiting ||
                          startChange.IsWaiting;

        if (!wasWaiting)
            return;

        readyChange.Tick();
        doorChange.Tick();
        difficultyChange.Tick();
        startChange.Tick();

        if (readyChange.IsWaiting ||
            doorChange.IsWaiting ||
            difficultyChange.IsWaiting ||
            startChange.IsWaiting)
        {
            return;
        }

        RefreshButtons();
        RefreshDifficulty();
    }

    private void OnDestroy()
    {
        screen?.UnregisterCallback<NavigationCancelEvent>(HandleCancelPressed);
        confirmPanel?.UnregisterCallback<ClickEvent>(HandleConfirmBackdropClicked);
        roomSettingsPanel?.UnregisterCallback<ClickEvent>(HandleRoomSettingsBackdropClicked);
        Unsubscribe();
        Dispose();
    }

    // Topmost first. The two dialogs cannot normally be open together - an
    // overlay takes every click aimed past it - but the order is stated rather
    // than left to whichever check came first, because closing the wrong one is
    // not something a player can undo by pressing the key again.
    private void HandleCancelPressed(NavigationCancelEvent evt)
    {
        if (pendingAction != PendingAction.None)
        {
            CancelPendingAction();
            evt.StopPropagation();
            return;
        }

        if (!isRoomSettingsOpen)
            return;

        CloseRoomSettings();
        evt.StopPropagation();
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
                Debug.LogError($"{nameof(LobbyUI)} has no document to bind.", this);

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
        roster = root.Q<VisualElement>("Roster");
        confirmPanel = root.Q<VisualElement>("ConfirmPanel");
        matchTransitionPanel = root.Q<VisualElement>("MatchTransitionPanel");
        roomSettingsPanel = root.Q<VisualElement>("RoomSettingsPanel");
        playerCountLabel = root.Q<Label>("PlayerCount");
        readyCountLabel = root.Q<Label>("ReadyCount");
        difficultyDescriptionLabel = root.Q<Label>("DifficultyDescription");
        difficultyOwnerNoteLabel = root.Q<Label>("DifficultyOwnerNote");
        doorHintLabel = root.Q<Label>("DoorHint");
        doorStatusLabel = root.Q<Label>("DoorStatus");
        setupDifficultyLabel = root.Q<Label>("SetupDifficulty");
        setupOwnerLabel = root.Q<Label>("SetupOwner");
        setupNoticeLabel = root.Q<Label>("SetupNotice");
        addressLabel = root.Q<Label>("Address");
        addressButton = root.Q<Button>("CopyAddressButton");
        addressCopyIcon = root.Q<Image>("AddressCopyIcon");
        addressField = root.Q<VisualElement>("AddressField");
        startHintLabel = root.Q<Label>("StartHint");
        confirmTextLabel = root.Q<Label>("ConfirmText");
        matchTransitionStageLabel = root.Q<Label>("MatchTransitionStage");
        matchTransitionTextLabel = root.Q<Label>("MatchTransitionText");
        matchTransitionDetailLabel = root.Q<Label>("MatchTransitionDetail");
        matchTransitionElapsedLabel = root.Q<Label>("MatchTransitionElapsed");
        difficultyField = root.Q<DropdownField>("Difficulty");
        addressPickField = root.Q<DropdownField>("AddressPick");
        visibilityToggle = root.Q<Toggle>("Visibility");
        readyButton = root.Q<Button>("ReadyButton");
        startButton = root.Q<Button>("StartButton");
        leaveButton = root.Q<Button>("LeaveButton");
        confirmButton = root.Q<Button>("ConfirmButton");
        confirmCancelButton = root.Q<Button>("ConfirmCancelButton");
        transitionLeaveButton = root.Q<Button>("TransitionLeaveButton");
        roomSettingsButton = root.Q<Button>("RoomSettingsButton");
        roomSettingsCloseButton = root.Q<Button>("RoomSettingsCloseButton");

        if (screen == null)
        {
            if (complainIfMissing)
                Debug.LogError($"{nameof(LobbyUI)} did not find 'Screen'.", this);

            return false;
        }

        // Escape, and the cancel button on a pad. Only a dialog answers it:
        // backing out of a lobby is leaving it, and that is too large a thing
        // to happen because somebody reached for the key that closes windows.
        screen.RegisterCallback<NavigationCancelEvent>(HandleCancelPressed);
        confirmPanel?.RegisterCallback<ClickEvent>(HandleConfirmBackdropClicked);
        roomSettingsPanel?.RegisterCallback<ClickEvent>(HandleRoomSettingsBackdropClicked);

        PopulateDifficultyChoices();
        PopulateAddressChoices();
        Subscribe();
        CancelPendingAction();
        SetRoomSettingsOpen(false, restoreFocus: false);
        SetMatchTransitionVisible(false);

        if (addressCopyIcon != null)
            addressCopyIcon.scaleMode = ScaleMode.ScaleToFit;

        return true;
    }

    private void Subscribe()
    {
        if (readyButton != null)
            readyButton.clicked += HandleReadyClicked;

        if (startButton != null)
            startButton.clicked += HandleStartGameClicked;

        if (leaveButton != null)
            leaveButton.clicked += HandleLeaveLobbyClicked;

        if (transitionLeaveButton != null)
            transitionLeaveButton.clicked += HandleTransitionLeaveClicked;

        if (visibilityToggle != null)
            visibilityToggle.RegisterValueChangedCallback(HandleVisibilityChanged);

        if (confirmButton != null)
            confirmButton.clicked += HandleConfirmed;

        if (confirmCancelButton != null)
            confirmCancelButton.clicked += CancelPendingAction;

        if (addressButton != null)
            addressButton.clicked += CopyAddress;

        if (difficultyField != null)
            difficultyField.RegisterValueChangedCallback(HandleDifficultyChanged);

        if (addressPickField != null)
            addressPickField.RegisterValueChangedCallback(HandleAddressPicked);

        if (roomSettingsButton != null)
            roomSettingsButton.clicked += OpenRoomSettings;

        if (roomSettingsCloseButton != null)
            roomSettingsCloseButton.clicked += CloseRoomSettings;
    }

    private void Unsubscribe()
    {
        if (readyButton != null)
            readyButton.clicked -= HandleReadyClicked;

        if (startButton != null)
            startButton.clicked -= HandleStartGameClicked;

        if (leaveButton != null)
            leaveButton.clicked -= HandleLeaveLobbyClicked;

        if (transitionLeaveButton != null)
            transitionLeaveButton.clicked -= HandleTransitionLeaveClicked;

        if (visibilityToggle != null)
            visibilityToggle.UnregisterValueChangedCallback(HandleVisibilityChanged);

        if (confirmButton != null)
            confirmButton.clicked -= HandleConfirmed;

        if (confirmCancelButton != null)
            confirmCancelButton.clicked -= CancelPendingAction;

        if (addressButton != null)
            addressButton.clicked -= CopyAddress;

        if (difficultyField != null)
            difficultyField.UnregisterValueChangedCallback(HandleDifficultyChanged);

        if (addressPickField != null)
            addressPickField.UnregisterValueChangedCallback(HandleAddressPicked);

        if (roomSettingsButton != null)
            roomSettingsButton.clicked -= OpenRoomSettings;

        if (roomSettingsCloseButton != null)
            roomSettingsCloseButton.clicked -= CloseRoomSettings;
    }

    private void HandleReadyClicked()
    {
        // What the room will look like if the server agrees. Read before the
        // command goes out, because the answer is the only thing that will
        // change it.
        bool wanted = !(readService.TryGetLocalPlayer(out LobbyPlayerData localPlayer) &&
                        localPlayer.IsReady);

        readyChange.Begin(
            () => readService.TryGetLocalPlayer(out LobbyPlayerData player) &&
                  player.IsReady == wanted,
            pendingChangeTimeoutSeconds);

        ReadyClicked?.Invoke();
        RefreshButtons();
    }

    private void HandleStartGameClicked()
    {
        startChange.Begin(
            () => readService.Phase != LobbyPhase.Open,
            pendingChangeTimeoutSeconds);

        StartGameClicked?.Invoke();
        RefreshButtons();
    }

    // For anybody else this is walking out of a room that carries on without
    // them. For the host it is a shutdown: LeaveLobby ends the session, and
    // everybody standing in the lobby lands back in the menu. Same button, same
    // word, two very different things - so the host gets the other word, and is
    // asked.
    private void HandleLeaveLobbyClicked()
    {
        RequestLeaveLobby(leaveButton);
    }

    private void HandleTransitionLeaveClicked()
    {
        RequestLeaveLobby(transitionLeaveButton);
    }

    private void RequestLeaveLobby(Button focusTarget)
    {
        if (readService == null || !readService.IsLocalPlayerRoomOwner)
        {
            LeaveLobbyClicked?.Invoke();
            return;
        }

        AskToConfirm(
            PendingAction.CloseLobby,
            readService.PlayerCount > 1
                ? closeLobbyConfirmText
                : closeLobbyConfirmAloneText,
            closeLobbyActionConfirmText,
            focusTarget);
    }

    // Every address the machine has, best first, with the network each one is
    // on written beside it - "LAN - Ethernet", "VPN - Radmin VPN". A machine
    // with a VPN client installed has two addresses that both look like an
    // address and reach different people, and which one is wanted is a
    // question only the host can answer.
    //
    // The picker only appears when there is a choice to make: on a machine
    // with one address it would be a control whose every state is the same. It
    // carries no label of its own - every dropdown in this game is named by a
    // Label beside it, and the entries here say what they are.
    private void PopulateAddressChoices()
    {
        addressOptions = LanAddressProvider.GetAll();

        if (addressPickField == null)
            return;

        List<string> labels = new List<string>(addressOptions.Count);

        foreach (LanAddressProvider.Option option in addressOptions)
            labels.Add(option.Label);

        addressPickField.choices = labels;
        addressPickField.SetValueWithoutNotify(labels.Count > 0 ? labels[0] : string.Empty);

        addressPickField.style.display = labels.Count > 1
            ? DisplayStyle.Flex
            : DisplayStyle.None;
    }

    // What the copy button copies and what the hint reads out: whichever
    // network the host chose, or the best guess if they never had to choose.
    private string SelectedAddress()
    {
        int index = addressPickField == null ? 0 : addressPickField.index;

        return index >= 0 && index < addressOptions.Count
            ? addressOptions[index].Address
            : LanAddressProvider.Get();
    }

    private void HandleAddressPicked(ChangeEvent<string> evt)
    {
        // A copy confirmation belongs to the address that was copied. Picking
        // another one makes it a lie.
        isAddressCopyFeedbackVisible = false;
        addressCopyFeedbackVersion++;

        // The address on its own, then the sentence around it. The first works
        // before a lobby exists, which is the state this screen briefly has.
        RefreshAddressText();
        Refresh();
    }

    // The address is meant to be handed to somebody, and reading four numbers
    // and three dots down a voice call is the worst way to do that. One click
    // puts it where a message can be pasted from.
    private void CopyAddress()
    {
        string address = SelectedAddress();

        if (string.IsNullOrEmpty(address))
            return;

        GUIUtility.systemCopyBuffer = address;

        if (addressButton == null)
            return;

        // The filled icon is confirmation without replacing the address the
        // player may still be reading. A version keeps repeated clicks from
        // letting the first scheduled reset cut the second confirmation short.
        isAddressCopyFeedbackVisible = true;
        int feedbackVersion = ++addressCopyFeedbackVersion;
        RefreshAddressText();

        addressButton.schedule
            .Execute(() => HideAddressCopyFeedback(feedbackVersion))
            .StartingIn(1200);
    }

    private void RefreshAddressText()
    {
        string address = SelectedAddress();
        bool hasAddress = !string.IsNullOrEmpty(address);

        if (!hasAddress)
            isAddressCopyFeedbackVisible = false;

        if (addressLabel != null)
            addressLabel.text = hasAddress ? address : addressUnknownText;

        if (addressButton != null)
        {
            addressButton.tooltip = isAddressCopyFeedbackVisible
                ? addressCopiedText
                : addressCopyTooltip;
            addressButton.SetEnabled(hasAddress);
        }

        if (addressCopyIcon != null)
        {
            addressCopyIcon.image = isAddressCopyFeedbackVisible
                ? copiedAddressIcon
                : copyAddressIcon;
        }
    }

    private void HideAddressCopyFeedback(int feedbackVersion)
    {
        if (feedbackVersion != addressCopyFeedbackVersion)
            return;

        isAddressCopyFeedbackVisible = false;
        RefreshAddressText();
    }

    private void HandleVisibilityChanged(ChangeEvent<bool> evt)
    {
        bool wanted = evt.newValue;

        doorChange.Begin(
            () => readService.Settings.IsPublic == wanted,
            pendingChangeTimeoutSeconds);

        LobbyVisibilityToggleClicked?.Invoke();
        RefreshDoor(readService.Phase == LobbyPhase.Open);
    }

    // The index is read off the field rather than out of the event, which
    // carries the label. Two difficulties are allowed to be called the same
    // thing, and a lobby is a bad place to find out that they were.
    private void HandleDifficultyChanged(ChangeEvent<string> evt)
    {
        int optionIndex = difficultyField != null ? difficultyField.index : -1;

        if (optionIndex < 0 || optionIndex >= difficultyIds.Length)
            return;

        int difficultyId = difficultyIds[optionIndex];

        // Nobody is ready, so nothing is being taken away and there is nothing
        // to ask about. A dialog that appears whether or not it matters is one
        // the host learns to dismiss without reading.
        if (CountReady() == 0)
        {
            SendDifficulty(difficultyId);
            return;
        }

        pendingDifficultyId = difficultyId;

        AskToConfirm(
            PendingAction.ChangeDifficulty,
            string.Format(difficultyChangeConfirmFormat, DifficultyName(difficultyId)),
            difficultyChangeActionText,
            roomSettingsCloseButton);
    }

    private void SendDifficulty(int difficultyId)
    {
        difficultyChange.Begin(
            () => readService.Settings.DifficultyId == difficultyId,
            pendingChangeTimeoutSeconds);

        DifficultySelected?.Invoke(difficultyId);
        RefreshDifficulty();
    }

    private void Refresh()
    {
        if (readService == null || boundRoot == null)
            return;

        readyChange.Tick();
        doorChange.Tick();
        difficultyChange.Tick();
        startChange.Tick();

        RefreshPlayers();
        RefreshButtons();
        RefreshDifficulty();
        RefreshMatchSetup();
        RefreshMatchTransition();
    }

    // The terms of the match, on the wall of the room rather than behind a
    // button. A player pressing Ready was agreeing to conditions they had not
    // been shown, which is the whole of why this exists.
    //
    // It also notices when the terms move. The server clears everybody's
    // readiness when they do - see LobbyController - and said nothing about it,
    // so from a player's side their own Ready simply came undone. Nothing
    // announces the change over the wire, so it is spotted the only way it can
    // be: by remembering what the room said last time.
    private void RefreshMatchSetup()
    {
        // ponytail: difficulty only. Map and mode belong on this block the day
        // either of them has more than one value - LobbySettingsData already
        // carries both ids, and GameMapCatalog can name a map - but a line
        // reading "Map: The House" on a game with one house is furniture.
        int difficultyId = readService.Settings.DifficultyId;

        if (setupDifficultyLabel != null)
        {
            setupDifficultyLabel.text = string.Format(
                matchDifficultyFormat,
                DifficultyName(difficultyId));
        }

        RefreshSetupOwner();

        // The first look is not a change. A screen that opened onto a room
        // already set to Hard has not been told anything.
        if (hasSeenSettings && difficultyId != lastSeenDifficultyId)
            AnnounceDifficultyChange(difficultyId);

        lastSeenDifficultyId = difficultyId;
        hasSeenSettings = true;
    }

    // Who is allowed to move any of it, answered before the player goes looking
    // for a control that is not theirs.
    private void RefreshSetupOwner()
    {
        if (setupOwnerLabel == null)
            return;

        if (readService.IsLocalPlayerRoomOwner)
        {
            setupOwnerLabel.text = settingsYoursText;
            return;
        }

        string ownerName = ResolvePlayerName(readService.RoomOwnerClientId);

        setupOwnerLabel.text = string.IsNullOrWhiteSpace(ownerName)
            ? settingsOwnerUnknownText
            : string.Format(settingsOwnerFormat, ownerName);
    }

    // Said in the words of whoever did it. The reset is stated flatly rather
    // than worked out: the server clears readiness every time the terms move,
    // whether or not anybody had pressed Ready, so the sentence is true in an
    // empty room as well - and asking the screen to decide would mean racing
    // two replicated values to find out which arrived first.
    private void AnnounceDifficultyChange(int difficultyId)
    {
        if (setupNoticeLabel == null)
            return;

        string format = readService.IsLocalPlayerRoomOwner
            ? difficultyChangedByYouFormat
            : difficultyChangedByHostFormat;

        setupNoticeLabel.text = string.Format(format, DifficultyName(difficultyId));
        setupNoticeLabel.style.display = DisplayStyle.Flex;

        int version = ++setupNoticeVersion;

        setupNoticeLabel.schedule
            .Execute(() => HideSetupNotice(version))
            .StartingIn((long)(Mathf.Max(1f, setupNoticeSeconds) * 1000f));
    }

    private void HideSetupNotice(int version)
    {
        if (version != setupNoticeVersion || setupNoticeLabel == null)
            return;

        setupNoticeLabel.text = string.Empty;
        setupNoticeLabel.style.display = DisplayStyle.None;
    }

    private string DifficultyName(int difficultyId)
    {
        for (int i = 0; i < difficultyIds.Length; i++)
        {
            if (difficultyIds[i] != difficultyId)
                continue;

            if (difficultyField?.choices != null && i < difficultyField.choices.Count)
                return difficultyField.choices[i];

            break;
        }

        return difficultyId.ToString();
    }

    private void PopulateDifficultyChoices()
    {
        if (difficultyField == null)
            return;

        if (difficultyCatalog == null)
        {
            difficultyField.style.display = DisplayStyle.None;
            return;
        }

        int count = difficultyCatalog.Count;
        difficultyIds = new int[count];
        difficultyDescriptions = new string[count];
        List<string> optionLabels = new List<string>(count);

        for (int i = 0; i < count; i++)
        {
            if (!difficultyCatalog.TryGetEntryAt(
                    i,
                    out EnemyDifficultyCatalog.EnemyDifficultyEntry entry))
            {
                continue;
            }

            difficultyIds[i] = entry.DifficultyId;
            difficultyDescriptions[i] = entry.Description;
            optionLabels.Add(entry.DisplayName);
        }

        difficultyField.choices = optionLabels;
    }

    // Everyone sees the choice, only the owner can move it. SetValueWithoutNotify
    // keeps the replicated value from bouncing straight back as a new command.
    private void RefreshDifficulty()
    {
        if (difficultyField == null || difficultyIds.Length == 0)
            return;

        bool canChangeDifficulty =
            readService.Phase == LobbyPhase.Open && readService.IsLocalPlayerRoomOwner;

        difficultyField.SetEnabled(canChangeDifficulty && !difficultyChange.IsWaiting);

        int selectedDifficultyId = readService.Settings.DifficultyId;

        for (int i = 0; i < difficultyIds.Length; i++)
        {
            if (difficultyIds[i] != selectedDifficultyId)
                continue;

            // Left where the host put it while the answer is out, for the same
            // reason the door's switch is: a list that snaps back to the old
            // value and then forward again reads as a choice being refused.
            if (!difficultyChange.IsWaiting &&
                difficultyField.choices != null &&
                i < difficultyField.choices.Count)
            {
                difficultyField.SetValueWithoutNotify(difficultyField.choices[i]);
            }

            SetDifficultyDescription(difficultyDescriptions[i], canChangeDifficulty);
            return;
        }
    }

    // A greyed out control with no reason given reads as broken rather than as
    // somebody else's to move, so the reason goes next to the description.
    //
    // On its own line rather than appended to the description: the description
    // is the one thing here that changes length as the dropdown moves, and it
    // keeps a fixed height so the dialog stops resizing under the pointer.
    // Hanging a constant sentence off the end of it would have paid for that
    // sentence in reserved room the host never uses.
    private void SetDifficultyDescription(string description, bool canChangeDifficulty)
    {
        if (difficultyDescriptionLabel != null)
            difficultyDescriptionLabel.text = description;

        if (difficultyOwnerNoteLabel == null)
            return;

        // The line under the list says three different things, and only one of
        // them at a time: that the answer is still out, that none came back, or
        // that this is not yours to move. The first two are about the press
        // that was just made and outrank the standing fact.
        if (difficultyChange.IsWaiting || difficultyChange.HasFailed)
        {
            difficultyOwnerNoteLabel.text = difficultyChange.IsWaiting
                ? pendingChangeText
                : pendingFailedText;

            difficultyOwnerNoteLabel.style.display = DisplayStyle.Flex;
            return;
        }

        difficultyOwnerNoteLabel.text = ownerOnlySettingText;
        difficultyOwnerNoteLabel.style.display =
            canChangeDifficulty ? DisplayStyle.None : DisplayStyle.Flex;
    }

    private void RefreshPlayers()
    {
        int readyCount = CountReady();

        // Before the rows, and regardless of them: a full lobby is the reason a
        // friend is turned away, and they find that out at connect time unless
        // somebody in here can see it coming.
        if (playerCountLabel != null)
        {
            playerCountLabel.text = string.Format(
                playerCountFormat,
                readService.PlayerCount,
                readService.Settings.MaxPlayers);
        }

        if (readyCountLabel != null)
        {
            readyCountLabel.text = string.Format(readyCountFormat, readyCount);
            readyCountLabel.EnableInClassList(
                ReadyCompleteClass,
                readService.PlayerCount > 0 &&
                readyCount == readService.PlayerCount);
        }

        if (roster == null)
            return;

        // Not knowing which player is us is reason enough to offer no kick at
        // all; the server refuses a self-kick anyway, but a button that cannot
        // work should not be there in the first place.
        bool hasLocalPlayer =
            readService.TryGetLocalPlayer(out LobbyPlayerData localPlayer);
        bool canKick = hasLocalPlayer &&
                       readService.IsLocalPlayerRoomOwner &&
                       readService.Phase == LobbyPhase.Open;
        int playerCount = readService.PlayerCount;

        // The row the question was about can leave while the question is still
        // on screen, and answering it then would remove whoever took their
        // place. Closing the lobby is about the room rather than a row, so it
        // survives the list changing under it.
        if (pendingAction == PendingAction.Kick &&
            string.IsNullOrEmpty(ResolvePlayerName(pendingKickClientId)))
        {
            CancelPendingAction();
        }

        // Rebuilt rather than rebound. The uGUI version kept its rows and hid
        // the spares because each one was a GameObject; these are small visual
        // element trees, and the list only changes when lobby state changes.
        roster.Clear();

        if (playerCount == 0)
        {
            Label empty = new Label(emptyRosterText);

            empty.AddToClassList("roster__empty");
            roster.Add(empty);
            return;
        }

        for (int i = 0; i < playerCount; i++)
        {
            LobbyPlayerData player = readService.GetPlayer(i);

            roster.Add(BuildPlayerRow(
                player,
                player.ClientId == readService.RoomOwnerClientId,
                hasLocalPlayer && player.ClientId == localPlayer.ClientId,
                canKick && player.ClientId != localPlayer.ClientId));
        }

        // The sound binder listens for the whole document and cannot hear
        // elements that did not exist when it looked. Every other screen is
        // built once from markup and never needs to say anything; this one
        // makes a kick button per player, and a control that is silent under
        // the pointer reads as one that cannot be used.
        sounds?.Bind();
    }

    private VisualElement BuildPlayerRow(
        LobbyPlayerData player,
        bool isRoomOwner,
        bool isLocalPlayer,
        bool canKick)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("roster__row");

        // Four names and no way to tell which one answers to you. Everything
        // else on this screen is about your own state - are you ready, may you
        // start - and the list was the one place that never said which row
        // that was.
        if (isLocalPlayer)
            row.AddToClassList("roster__row--you");

        Label name = new Label(isLocalPlayer
            ? player.PlayerName + localPlayerSuffix
            : player.PlayerName.ToString());

        name.AddToClassList("roster__name");
        row.Add(name);

        VisualElement badges = new VisualElement();
        badges.AddToClassList("roster__badges");

        // Ownership and readiness are independent facts. The old row replaced
        // the host's ready state with "Owner", while the start rule still
        // counted that state; clients were told to wait for somebody the list
        // could never identify.
        if (isRoomOwner)
        {
            Label role = new Label(ownerStatusText);
            role.AddToClassList("roster__role");
            badges.Add(role);
        }

        Label status = new Label(player.IsReady
            ? readyStatusText
            : notReadyStatusText);

        status.AddToClassList("roster__status");

        if (player.IsReady)
            status.AddToClassList("roster__status--ready");

        badges.Add(status);
        row.Add(badges);

        if (canKick)
        {
            // Captured once per row rather than read back off the list. Rows
            // are rebuilt whenever anything changes, so a handler that looked
            // up an index would be pointing at whoever took that place.
            ulong clientId = player.ClientId;
            Button kick = null;
            kick = new Button(() => HandlePlayerKickRequested(clientId, kick))
            {
                text = kickActionText
            };

            kick.AddToClassList("button");
            kick.AddToClassList("roster__kick");
            row.Add(kick);
        }

        return row;
    }

    private void HandlePlayerKickRequested(ulong clientId, Button focusTarget)
    {
        if (confirmPanel == null)
        {
            PlayerKickRequested?.Invoke(clientId);
            return;
        }

        pendingKickClientId = clientId;

        AskToConfirm(
            PendingAction.Kick,
            string.Format(kickConfirmFormat, ResolvePlayerName(clientId)),
            kickActionText,
            focusTarget);
    }

    private void AskToConfirm(
        PendingAction action,
        string question,
        string actionLabel,
        Button focusTarget)
    {
        pendingAction = action;
        pendingFocusTarget = focusTarget;

        if (confirmTextLabel != null)
            confirmTextLabel.text = question;

        // The button says what it does rather than saying Yes. A dialog whose
        // answer is Yes makes the reader hold the question in their head to
        // work out what they are agreeing to.
        if (confirmButton != null)
            confirmButton.text = actionLabel;

        UiFade.Set(confirmPanel, true, OverlayOpenClass);

        // The safe answer takes the focus, not the one being asked about. Both
        // questions this dialog asks end something that cannot be undone, and
        // a dialog that arrives with Enter aimed at the irreversible half is
        // worse than no dialog at all.
        if (confirmCancelButton != null)
            confirmCancelButton.schedule.Execute(() => confirmCancelButton.Focus());
    }

    private void HandleConfirmed()
    {
        PendingAction action = pendingAction;
        ulong clientId = pendingKickClientId;

        ClosePendingAction(restoreFocus: false);

        if (action == PendingAction.Kick)
        {
            PlayerKickRequested?.Invoke(clientId);
            screen?.schedule.Execute(() => readyButton?.Focus());
        }
        else if (action == PendingAction.CloseLobby)
            LeaveLobbyClicked?.Invoke();
        else if (action == PendingAction.ChangeDifficulty)
        {
            SendDifficulty(pendingDifficultyId);

            // The host is still standing in the room settings dialog, and the
            // dialog they answered took the focus with it when it closed.
            roomSettingsCloseButton?.schedule
                .Execute(() => roomSettingsCloseButton.Focus());
        }
    }

    private void CancelPendingAction()
    {
        // The dropdown has already moved to the answer the host is about to
        // take back, and nothing else will move it: the room's difficulty never
        // changed, so no update is coming to put it right.
        bool wasChangingDifficulty = pendingAction == PendingAction.ChangeDifficulty;

        ClosePendingAction(restoreFocus: true);

        if (wasChangingDifficulty)
            RefreshDifficulty();
    }

    private void ClosePendingAction(bool restoreFocus)
    {
        bool wasOpen = pendingAction != PendingAction.None;
        Button focusTarget = pendingFocusTarget;

        pendingAction = PendingAction.None;
        pendingFocusTarget = null;
        UiFade.Set(confirmPanel, false, OverlayOpenClass);

        if (!restoreFocus || !wasOpen || screen == null)
            return;

        // Kick buttons are rebuilt with the roster. Resolve the target when
        // the scheduled focus actually runs and fall back to a stable action
        // if the row disappeared while the question was open.
        screen.schedule.Execute(() =>
        {
            Button target = focusTarget != null && focusTarget.panel != null
                ? focusTarget
                : readyButton;

            target?.Focus();
        });
    }

    private void HandleConfirmBackdropClicked(ClickEvent evt)
    {
        if (!ReferenceEquals(evt.target, confirmPanel) ||
            pendingAction == PendingAction.None)
        {
            return;
        }

        CancelPendingAction();
    }

    // What is left of the room's settings, a click away instead of down the
    // column: difficulty, and nothing else.
    //
    // The lobby has 675 logical pixels of height on a sixteen-by-nine screen -
    // the panel scales to the width of the window, so the height is whatever
    // the aspect ratio leaves and no monitor buys more of it - so the column
    // holds what a host looks at and this holds what they set once.
    //
    // The door and the invite address came back out. They read like settings
    // and they are not: they are the state of the room, they are the first
    // thing that goes wrong for two people trying to play, and a host cannot
    // be expected to open a page to find out why nobody can reach them.
    private void OpenRoomSettings()
    {
        SetRoomSettingsOpen(true, restoreFocus: false);
    }

    private void CloseRoomSettings()
    {
        SetRoomSettingsOpen(false, restoreFocus: true);
    }

    private void SetRoomSettingsOpen(bool open, bool restoreFocus)
    {
        bool wasOpen = isRoomSettingsOpen;

        isRoomSettingsOpen = open;
        UiFade.Set(roomSettingsPanel, open, OverlayOpenClass);

        if (open)
        {
            // Done takes the focus rather than the first setting. Nothing in
            // here has to be answered, and the way out is what somebody who
            // opened it for a look wants next.
            roomSettingsCloseButton?.schedule
                .Execute(() => roomSettingsCloseButton.Focus());

            return;
        }

        if (!restoreFocus || !wasOpen)
            return;

        roomSettingsButton?.schedule.Execute(() => roomSettingsButton.Focus());
    }

    private void HandleRoomSettingsBackdropClicked(ClickEvent evt)
    {
        if (!ReferenceEquals(evt.target, roomSettingsPanel) || !isRoomSettingsOpen)
            return;

        CloseRoomSettings();
    }

    private string ResolvePlayerName(ulong clientId)
    {
        for (int i = 0; i < readService.PlayerCount; i++)
        {
            LobbyPlayerData player = readService.GetPlayer(i);

            if (player.ClientId == clientId)
                return player.PlayerName.ToString();
        }

        return string.Empty;
    }

    private void RefreshButtons()
    {
        bool isLobbyPhaseOpen = readService.Phase == LobbyPhase.Open;

        if (startButton != null)
        {
            startButton.style.display = readService.IsLocalPlayerRoomOwner
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            // Pressable once. The phase does not change on the host's own word,
            // and until it does the button was still live - so a host who
            // pressed it twice asked the server to start the match twice.
            startButton.SetEnabled(
                isLobbyPhaseOpen && readService.CanStartGame && !startChange.IsWaiting);

            startButton.text = startChange.IsWaiting
                ? startPendingActionText
                : startActionText;
        }

        bool hasLocalPlayer = readService.TryGetLocalPlayer(out LobbyPlayerData localPlayer);
        bool isLocalPlayerReady = hasLocalPlayer && localPlayer.IsReady;

        if (readyButton != null)
        {
            // Nothing to press while the last press is still unanswered, and
            // the word says which of the two states is being waited on rather
            // than claiming either of them.
            readyButton.SetEnabled(
                isLobbyPhaseOpen && hasLocalPlayer && !readyChange.IsWaiting);

            readyButton.text = readyChange.IsWaiting
                ? pendingChangeText
                : isLocalPlayerReady
                    ? standDownActionText
                    : readyActionText;
        }

        // The lit entry is the thing to do next, and for a host that is two
        // different things in turn. Start was lit from the moment the lobby
        // opened, which put the brightest word in the column on a button the
        // host could not press yet and left the one they could - their own
        // Ready - reading as an afterthought.
        //
        // So it moves: Ready while they have not answered for themselves,
        // Start once they have. Everybody else has no Start button at all, and
        // for them the column simply goes quiet once they are ready, which is
        // true - what happens next is not theirs to do.
        readyButton?.EnableInClassList(ConfirmToneClass, !isLocalPlayerReady);
        startButton?.EnableInClassList(ConfirmToneClass, isLocalPlayerReady);

        if (leaveButton != null)
        {
            leaveButton.text = readService.IsLocalPlayerRoomOwner
                ? closeLobbyActionText
                : leaveActionText;
        }

        RefreshStartHint(isLobbyPhaseOpen);

        RefreshDoor(isLobbyPhaseOpen);
    }

    // A lobby starts shut, on purpose, so a host can set the room up before
    // anybody walks into it. Nothing said so, and nothing said what to type to
    // reach it either, so the two walls between a host and their friend were
    // both invisible and the second one only showed up after the first was
    // guessed past.
    //
    // They are one block on the wall of the room now rather than a page behind
    // a button: the state in words, the switch that changes it, the address to
    // send, and - on a machine that has more than one - which network that
    // address is on.
    private void RefreshDoor(bool isLobbyPhaseOpen)
    {
        bool isOwner = readService.IsLocalPlayerRoomOwner;
        bool isPublic = readService.Settings.IsPublic;

        // Said out loud, and read by everybody. A player whose friend cannot
        // get in needs to know why as much as the host does.
        if (doorStatusLabel != null)
        {
            doorStatusLabel.text = doorChange.IsWaiting
                ? pendingChangeText
                : doorChange.HasFailed
                    ? pendingFailedText
                    : isPublic ? doorOpenStatusText : doorShutStatusText;

            // Neither open nor shut while the answer is out, and a failure is
            // not a state of the door either.
            doorStatusLabel.EnableInClassList(
                DoorOpenClass,
                isPublic && !doorChange.IsWaiting && !doorChange.HasFailed);
        }

        // Only the host can move it, and only while the lobby is still a lobby.
        if (visibilityToggle != null)
        {
            // Left where the host put it while the answer is out. Setting it
            // from the room would flick it back to where it was and then
            // forward again when the reply lands, which reads as the switch
            // refusing the press.
            if (!doorChange.IsWaiting)
                visibilityToggle.SetValueWithoutNotify(isPublic);

            visibilityToggle.SetEnabled(
                isOwner && isLobbyPhaseOpen && !doorChange.IsWaiting);
        }

        // Shown to the host alone. Everybody else reached this screen by
        // typing it.
        if (addressField != null)
            addressField.style.display = isOwner ? DisplayStyle.Flex : DisplayStyle.None;

        string address = SelectedAddress();

        RefreshAddressText();

        if (doorHintLabel == null)
            return;

        if (!isPublic)
        {
            doorHintLabel.text = isOwner ? doorShutText : doorNotYoursText;
            return;
        }

        // An open door on a full room is still a closed one, and approval
        // refuses on MaxPlayers whatever the door says. Promising that anybody
        // can join at this point sends two people off to find out why the
        // address does not work.
        if (readService.PlayerCount >= readService.Settings.MaxPlayers)
        {
            doorHintLabel.text = string.Format(doorFullFormat, readService.Settings.MaxPlayers);
            return;
        }

        if (!isOwner)
        {
            doorHintLabel.text = string.Empty;
            return;
        }

        // An open lobby on a machine with no address is the one case where the
        // door being open buys nothing, and the old line handed out the words
        // "no network" as though they were something to type.
        doorHintLabel.text = string.IsNullOrEmpty(address)
            ? doorOpenNoAddressText
            : string.Format(doorOpenFormat, address);
    }

    // The same reasons the server checks, in the same order, so the line never
    // claims something the rules do not. Shown to everybody: the players who
    // are holding the match up are the ones who need to hear it.
    private void RefreshStartHint(bool isLobbyPhaseOpen)
    {
        if (startHintLabel == null)
            return;

        LobbySettingsData settings = readService.Settings;
        bool hasLocalPlayer = readService.TryGetLocalPlayer(out LobbyPlayerData localPlayer);

        StartHint hint = ChooseStartHint(
            commandFailed: readyChange.HasFailed || startChange.HasFailed,
            isLobbyPhaseOpen: isLobbyPhaseOpen,
            missingPlayers: settings.MinPlayersToStart - readService.PlayerCount,
            notReadyCount: settings.RequireAllPlayersReady ? CountNotReady() : 0,
            isLocalPlayerReady: !hasLocalPlayer || localPlayer.IsReady,
            isLocalPlayerRoomOwner: readService.IsLocalPlayerRoomOwner);

        startHintLabel.text = Say(hint, settings);
    }

    // Which of the things this line can say is the true one. Pulled out of the
    // formatting because the order is the whole of it and the order is the part
    // that is easy to get wrong: every branch here is true at the same time as
    // two others, and which one a player is shown decides whether they read the
    // line as their problem or somebody else's.
    public enum StartHint
    {
        Starting,
        CommandRefused,
        NeedMorePlayers,
        ReadyUpYourself,
        WaitingForOne,
        WaitingForSeveral,
        EveryoneReady,
        WaitingForHost
    }

    public static StartHint ChooseStartHint(
        bool commandFailed,
        bool isLobbyPhaseOpen,
        int missingPlayers,
        int notReadyCount,
        bool isLocalPlayerReady,
        bool isLocalPlayerRoomOwner)
    {
        // The match is already going. Nothing below this is worth saying to
        // somebody watching a scene load.
        if (!isLobbyPhaseOpen)
            return StartHint.Starting;

        // A press that went nowhere - a Ready, or a Start. It outranks
        // everything the room has to say, because everything the room has to
        // say assumes the player's own answer got through, and this one did
        // not.
        if (commandFailed)
            return StartHint.CommandRefused;

        // An empty chair beats an unready player: readying up in a room that
        // cannot start either way is work with nothing at the end of it.
        if (missingPlayers > 0)
            return StartHint.NeedMorePlayers;

        if (notReadyCount > 0)
        {
            // Yourself first. Whatever else the room is waiting for, if one of
            // the people it is waiting for is you, that is the only part of it
            // you can do anything about.
            if (!isLocalPlayerReady)
                return StartHint.ReadyUpYourself;

            // One person gets named. A room of five with one name missing is a
            // room where four people know who to talk to; "Waiting for 1" is a
            // room where five people wait for somebody to own up.
            return notReadyCount == 1
                ? StartHint.WaitingForOne
                : StartHint.WaitingForSeveral;
        }

        // It used to go blank here, which left a hole above the buttons at the
        // exact moment the screen had the most to say. Everyone waiting on the
        // host should be told they are waiting on the host, and the host should
        // be told there is nothing left to wait for.
        return isLocalPlayerRoomOwner
            ? StartHint.EveryoneReady
            : StartHint.WaitingForHost;
    }

    private string Say(StartHint hint, LobbySettingsData settings)
    {
        switch (hint)
        {
            case StartHint.Starting:
                return startingText;

            case StartHint.CommandRefused:
                return pendingFailedText;

            case StartHint.NeedMorePlayers:
                return string.Format(
                    needMorePlayersFormat,
                    settings.MinPlayersToStart - readService.PlayerCount);

            case StartHint.ReadyUpYourself:
                return readyUpPromptText;

            // The name is looked up here rather than chosen above, because a
            // row can be in the roster before its name has arrived - and a
            // nameless player is counted rather than named.
            case StartHint.WaitingForOne:
                return TryGetOnlyNotReadyName(out string name)
                    ? string.Format(waitingForPlayerFormat, name)
                    : string.Format(waitingForPlayersFormat, 1);

            case StartHint.WaitingForSeveral:
                return string.Format(waitingForPlayersFormat, CountNotReady());

            case StartHint.EveryoneReady:
                return readyToStartText;

            default:
                return waitingForHostText;
        }
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
        RefreshMatchTransition();
    }

    // Starting a match changes two replicated lifecycles: LobbyPhase closes
    // interaction first, then the global session enters LoadingGame. Reading
    // both means every peer gets immediate feedback without guessing which
    // network callback will arrive first on that machine.
    private void RefreshMatchTransition()
    {
        if (readService == null || boundRoot == null)
            return;

        NetworkSessionState sessionState = sessionReadService != null
            ? sessionReadService.CurrentState
            : NetworkSessionState.Lobby;

        bool show = sessionState != NetworkSessionState.Offline &&
                    sessionState != NetworkSessionState.Failed &&
                    (readService.Phase == LobbyPhase.Starting ||
                     sessionState == NetworkSessionState.LoadingGame ||
                     sessionState == NetworkSessionState.InGame ||
                     sessionState == NetworkSessionState.Disconnecting);

        SetMatchTransitionVisible(show);

        if (!show)
            return;

        string stage;
        string message;
        string detail;

        switch (sessionState)
        {
            case NetworkSessionState.LoadingGame:
                stage = loadingMatchStageText;
                message = loadingMatchText;
                detail = ResolveMatchTransitionDetail();
                break;

            case NetworkSessionState.InGame:
                stage = enteringMatchStageText;
                message = enteringMatchText;
                detail = ResolveMatchTransitionDetail();
                break;

            case NetworkSessionState.Disconnecting:
                stage = leavingMatchStageText;
                message = leavingMatchText;
                detail = leavingTransitionDetail;
                break;

            default:
                stage = preparingMatchStageText;
                message = preparingMatchText;
                detail = ResolveMatchTransitionDetail();
                break;
        }

        if (matchTransitionStageLabel != null)
            matchTransitionStageLabel.text = stage;

        if (matchTransitionTextLabel != null)
            matchTransitionTextLabel.text = message;

        if (matchTransitionDetailLabel != null)
            matchTransitionDetailLabel.text = detail;
    }

    private string ResolveMatchTransitionDetail()
    {
        return readService != null && readService.IsLocalPlayerRoomOwner
            ? hostTransitionDetail
            : clientTransitionDetail;
    }

    private void SetMatchTransitionVisible(bool visible)
    {
        if (visible && !isMatchTransitionVisible)
        {
            matchTransitionStartedAt = Time.unscaledTime;
            shownTransitionSeconds = -1;

            if (matchTransitionElapsedLabel != null)
            {
                matchTransitionElapsedLabel.text = string.Format(
                    transitionElapsedFormat,
                    0);
            }
        }

        // Settings go away when the match starts. Two overlays fading over
        // each other is legible only because one was declared after the other,
        // and the room's difficulty stops being anybody's business the moment
        // the room stops being a lobby.
        if (visible)
            SetRoomSettingsOpen(false, restoreFocus: false);

        isMatchTransitionVisible = visible;
        panel?.SetEnabled(!visible);
        UiFade.Set(matchTransitionPanel, visible, OverlayOpenClass);
    }

    private int CountReady()
    {
        if (readService == null)
            return 0;

        int readyCount = 0;

        for (int i = 0; i < readService.PlayerCount; i++)
        {
            if (readService.GetPlayer(i).IsReady)
                readyCount++;
        }

        return readyCount;
    }

    private int CountNotReady()
    {
        return readService != null
            ? readService.PlayerCount - CountReady()
            : 0;
    }

    // Only worth asking when exactly one player is holding the room up, and it
    // still answers no if that player has no name yet - a row that has arrived
    // in the roster before its name has is a normal moment on this screen, and
    // "Waiting for " with nothing after it is worse than a number.
    private bool TryGetOnlyNotReadyName(out string name)
    {
        name = string.Empty;

        for (int i = 0; i < readService.PlayerCount; i++)
        {
            LobbyPlayerData player = readService.GetPlayer(i);

            if (player.IsReady)
                continue;

            name = player.PlayerName.ToString();
            break;
        }

        return !string.IsNullOrWhiteSpace(name);
    }
}
