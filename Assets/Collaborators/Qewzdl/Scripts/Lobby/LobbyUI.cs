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

    [Header("References")]
    [SerializeField] private UIDocument document;
    [SerializeField] private UiDocumentSounds sounds;
    [SerializeField] private EnemyDifficultyCatalog difficultyCatalog;

    [Header("Text")]
    // Both are verbs. The old pair said "Ready" and "Not ready", and the second
    // of those reads as a state rather than as the thing pressing it does -
    // which is the one question a button label exists to answer.
    [SerializeField] private string readyActionText = "Ready up";
    [SerializeField] private string standDownActionText = "Stand down";
    [SerializeField] private string playerCountFormat = "{0}/{1}";
    [SerializeField] private string kickConfirmFormat =
        "Remove {0}? They will not be able to join again this session.";
    [SerializeField] private string needMorePlayersFormat = "Need {0} more to start";
    [SerializeField] private string waitingForReadyFormat = "Waiting for {0} to get ready";
    [SerializeField] private string startingText = "Starting the match...";
    [SerializeField] private string ownerOnlySettingText = "Only the host can change this";
    [SerializeField] private string doorShutText =
        "Shut. Nobody can reach this lobby until you open it.";
    [SerializeField] private string doorOpenFormat =
        "Open. Anyone on this network can join by typing {0}.";
    [SerializeField] private string doorNotYoursText = "Only the host can open the door.";
    [SerializeField] private string addressUnknownText = "no network";
    [SerializeField] private string addressCopiedText = "copied";
    [SerializeField] private string doorFullFormat =
        "Open, but the room is full at {0}. Nobody else can get in.";
    [SerializeField] private string readyToStartText = "Everybody is ready";
    [SerializeField] private string waitingForHostText = "Waiting for the host to start";

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

    private ILobbyReadService readService;
    private int[] difficultyIds = Array.Empty<int>();
    private string[] difficultyDescriptions = Array.Empty<string>();

    private VisualElement boundRoot;
    private VisualElement screen;
    private VisualElement roster;
    private VisualElement confirmPanel;
    private Label playerCountLabel;
    private Label difficultyDescriptionLabel;
    private Label doorHintLabel;

    private VisualElement addressField;
    private Label startHintLabel;
    private Label confirmTextLabel;
    private DropdownField difficultyField;
    private Toggle visibilityToggle;
    private Button readyButton;
    private Button startButton;
    private Button leaveButton;
    private Button confirmButton;
    private Button confirmCancelButton;
    private Button addressButton;

    // Two things on this screen are worth asking about twice, and they are the
    // two that cannot be taken back: removing somebody for the rest of the
    // session, and closing a room three other people are standing in. One
    // dialog serves both - it is the same question with a different noun, and
    // a second overlay would have been the same markup twice.
    private enum PendingAction
    {
        None,
        Kick,
        CloseLobby
    }

    private PendingAction pendingAction;
    private ulong pendingKickClientId;

    public event Action ReadyClicked;
    public event Action StartGameClicked;
    public event Action LeaveLobbyClicked;
    public event Action<int> DifficultySelected;
    public event Action LobbyVisibilityToggleClicked;
    public event Action<ulong> PlayerKickRequested;

    public void Construct(ILobbyReadService readService)
    {
        if (this.readService != null)
            this.readService.LobbyChanged -= Refresh;

        this.readService = readService;

        if (this.readService != null)
            this.readService.LobbyChanged += Refresh;

        Show(complainIfMissing: false);
        Refresh();
    }

    public void Dispose()
    {
        if (readService != null)
            readService.LobbyChanged -= Refresh;

        readService = null;
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

    private void OnDestroy()
    {
        Unsubscribe();
        Dispose();
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
        screen = root.Q<VisualElement>("Screen");
        roster = root.Q<VisualElement>("Roster");
        confirmPanel = root.Q<VisualElement>("ConfirmPanel");
        playerCountLabel = root.Q<Label>("PlayerCount");
        difficultyDescriptionLabel = root.Q<Label>("DifficultyDescription");
        doorHintLabel = root.Q<Label>("DoorHint");
        addressButton = root.Q<Button>("Address");
        addressField = root.Q<VisualElement>("AddressField");
        startHintLabel = root.Q<Label>("StartHint");
        confirmTextLabel = root.Q<Label>("ConfirmText");
        difficultyField = root.Q<DropdownField>("Difficulty");
        visibilityToggle = root.Q<Toggle>("Visibility");
        readyButton = root.Q<Button>("ReadyButton");
        startButton = root.Q<Button>("StartButton");
        leaveButton = root.Q<Button>("LeaveButton");
        confirmButton = root.Q<Button>("ConfirmButton");
        confirmCancelButton = root.Q<Button>("ConfirmCancelButton");

        if (screen == null)
        {
            if (complainIfMissing)
                Debug.LogError($"{nameof(LobbyUI)} did not find 'Screen'.", this);

            return false;
        }

        PopulateDifficultyChoices();
        Subscribe();
        CancelPendingAction();

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
    }

    private void Unsubscribe()
    {
        if (readyButton != null)
            readyButton.clicked -= HandleReadyClicked;

        if (startButton != null)
            startButton.clicked -= HandleStartGameClicked;

        if (leaveButton != null)
            leaveButton.clicked -= HandleLeaveLobbyClicked;

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
    }

    private void HandleReadyClicked()
    {
        ReadyClicked?.Invoke();
    }

    private void HandleStartGameClicked()
    {
        StartGameClicked?.Invoke();
    }

    // For anybody else this is walking out of a room that carries on without
    // them. For the host it is a shutdown: LeaveLobby ends the session, and
    // everybody standing in the lobby lands back in the menu. Same button, same
    // word, two very different things - so the host gets the other word, and is
    // asked.
    private void HandleLeaveLobbyClicked()
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
            closeLobbyActionConfirmText);
    }

    // The address is meant to be handed to somebody, and reading four numbers
    // and three dots down a voice call is the worst way to do that. One click
    // puts it where a message can be pasted from.
    private void CopyAddress()
    {
        string address = LanAddressProvider.Get();

        if (string.IsNullOrEmpty(address))
            return;

        GUIUtility.systemCopyBuffer = address;

        if (addressButton == null)
            return;

        // Said on the button itself and taken back a moment later. A copy that
        // reports nothing looks exactly like a copy that did not happen.
        addressButton.text = addressCopiedText;
        addressButton.schedule.Execute(RefreshAddressText).StartingIn(1200);
    }

    private void RefreshAddressText()
    {
        if (addressButton == null)
            return;

        string address = LanAddressProvider.Get();

        addressButton.text = string.IsNullOrEmpty(address) ? addressUnknownText : address;
    }

    private void HandleVisibilityChanged(ChangeEvent<bool> evt)
    {
        LobbyVisibilityToggleClicked?.Invoke();
    }

    // The index is read off the field rather than out of the event, which
    // carries the label. Two difficulties are allowed to be called the same
    // thing, and a lobby is a bad place to find out that they were.
    private void HandleDifficultyChanged(ChangeEvent<string> evt)
    {
        int optionIndex = difficultyField != null ? difficultyField.index : -1;

        if (optionIndex < 0 || optionIndex >= difficultyIds.Length)
            return;

        DifficultySelected?.Invoke(difficultyIds[optionIndex]);
    }

    private void Refresh()
    {
        if (readService == null || boundRoot == null)
            return;

        RefreshPlayers();
        RefreshButtons();
        RefreshDifficulty();
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

        difficultyField.SetEnabled(canChangeDifficulty);

        int selectedDifficultyId = readService.Settings.DifficultyId;

        for (int i = 0; i < difficultyIds.Length; i++)
        {
            if (difficultyIds[i] != selectedDifficultyId)
                continue;

            if (difficultyField.choices != null && i < difficultyField.choices.Count)
                difficultyField.SetValueWithoutNotify(difficultyField.choices[i]);

            SetDifficultyDescription(difficultyDescriptions[i], canChangeDifficulty);
            return;
        }
    }

    // A greyed out control with no reason given reads as broken rather than as
    // somebody else's to move, so the reason goes next to the description.
    private void SetDifficultyDescription(string description, bool canChangeDifficulty)
    {
        if (difficultyDescriptionLabel == null)
            return;

        if (canChangeDifficulty)
        {
            difficultyDescriptionLabel.text = description;
            return;
        }

        difficultyDescriptionLabel.text = string.IsNullOrWhiteSpace(description)
            ? ownerOnlySettingText
            : description + Environment.NewLine + ownerOnlySettingText;
    }

    private void RefreshPlayers()
    {
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
        // the spares because each one was a GameObject; these are three
        // elements, a room holds four of them, and the list only changes when
        // a person presses something.
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

        Label status = new Label(isRoomOwner
            ? ownerStatusText
            : player.IsReady
                ? readyStatusText
                : notReadyStatusText);

        status.AddToClassList("roster__status");

        if (isRoomOwner)
            status.AddToClassList("roster__status--owner");
        else if (player.IsReady)
            status.AddToClassList("roster__status--ready");

        row.Add(status);

        if (canKick)
        {
            // Captured once per row rather than read back off the list. Rows
            // are rebuilt whenever anything changes, so a handler that looked
            // up an index would be pointing at whoever took that place.
            ulong clientId = player.ClientId;
            Button kick = new Button(() => HandlePlayerKickRequested(clientId))
            {
                text = kickActionText
            };

            kick.AddToClassList("button");
            kick.AddToClassList("roster__kick");
            row.Add(kick);
        }

        return row;
    }

    private void HandlePlayerKickRequested(ulong clientId)
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
            kickActionText);
    }

    private void AskToConfirm(PendingAction action, string question, string actionLabel)
    {
        pendingAction = action;

        if (confirmTextLabel != null)
            confirmTextLabel.text = question;

        // The button says what it does rather than saying Yes. A dialog whose
        // answer is Yes makes the reader hold the question in their head to
        // work out what they are agreeing to.
        if (confirmButton != null)
            confirmButton.text = actionLabel;

        UiFade.Set(confirmPanel, true, OverlayOpenClass);
    }

    private void HandleConfirmed()
    {
        PendingAction action = pendingAction;
        ulong clientId = pendingKickClientId;

        CancelPendingAction();

        if (action == PendingAction.Kick)
            PlayerKickRequested?.Invoke(clientId);
        else if (action == PendingAction.CloseLobby)
            LeaveLobbyClicked?.Invoke();
    }

    private void CancelPendingAction()
    {
        pendingAction = PendingAction.None;
        UiFade.Set(confirmPanel, false, OverlayOpenClass);
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

            startButton.SetEnabled(isLobbyPhaseOpen && readService.CanStartGame);
        }

        if (readyButton != null)
        {
            bool hasLocalPlayer = readService.TryGetLocalPlayer(out LobbyPlayerData localPlayer);

            readyButton.SetEnabled(isLobbyPhaseOpen && hasLocalPlayer);
            readyButton.text = hasLocalPlayer && localPlayer.IsReady
                ? standDownActionText
                : readyActionText;
        }

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
    // They are one thing on screen now: the address is only worth reading out
    // while the door is open, and the line under the switch says which of those
    // is true in words rather than leaving it to a tick box.
    private void RefreshDoor(bool isLobbyPhaseOpen)
    {
        bool isOwner = readService.IsLocalPlayerRoomOwner;
        bool isPublic = readService.Settings.IsPublic;

        // Everybody sees the state - a player whose friend cannot get in needs
        // to know why - and only the host can move it, and only while the lobby
        // is still a lobby.
        if (visibilityToggle != null)
        {
            visibilityToggle.SetValueWithoutNotify(isPublic);
            visibilityToggle.SetEnabled(isOwner && isLobbyPhaseOpen);
        }

        // Shown to the host alone. Everybody else reached this screen by
        // typing it.
        if (addressField != null)
            addressField.style.display = isOwner ? DisplayStyle.Flex : DisplayStyle.None;

        string address = LanAddressProvider.Get();

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

        doorHintLabel.text = isOwner
            ? string.Format(doorOpenFormat, string.IsNullOrEmpty(address)
                ? addressUnknownText
                : address)
            : string.Empty;
    }

    // The same reasons the server checks, in the same order, so the line never
    // claims something the rules do not. Shown to everybody: the players who
    // are holding the match up are the ones who need to hear it.
    private void RefreshStartHint(bool isLobbyPhaseOpen)
    {
        if (startHintLabel == null)
            return;

        if (!isLobbyPhaseOpen)
        {
            startHintLabel.text = startingText;
            return;
        }

        LobbySettingsData settings = readService.Settings;
        int missingPlayers = settings.MinPlayersToStart - readService.PlayerCount;

        if (missingPlayers > 0)
        {
            startHintLabel.text = string.Format(needMorePlayersFormat, missingPlayers);
            return;
        }

        int notReadyCount = settings.RequireAllPlayersReady ? CountNotReady() : 0;

        if (notReadyCount > 0)
        {
            startHintLabel.text = string.Format(waitingForReadyFormat, notReadyCount);
            return;
        }

        // It used to go blank here, which left a hole above the buttons at the
        // exact moment the screen had the most to say. Everyone waiting on the
        // host should be told they are waiting on the host, and the host should
        // be told there is nothing left to wait for.
        startHintLabel.text = readService.IsLocalPlayerRoomOwner
            ? readyToStartText
            : waitingForHostText;
    }

    private int CountNotReady()
    {
        int notReadyCount = 0;

        for (int i = 0; i < readService.PlayerCount; i++)
        {
            if (!readService.GetPlayer(i).IsReady)
                notReadyCount++;
        }

        return notReadyCount;
    }
}
