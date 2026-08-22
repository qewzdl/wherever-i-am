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
    [SerializeField] private string readyActionText = "Ready";
    [SerializeField] private string notReadyActionText = "Not ready";
    [SerializeField] private string playerCountFormat = "{0}/{1}";
    [SerializeField] private string kickConfirmFormat =
        "Remove {0}? They will not be able to join again this session.";
    [SerializeField] private string needMorePlayersFormat = "Need {0} more to start";
    [SerializeField] private string waitingForReadyFormat = "Waiting for {0} to get ready";
    [SerializeField] private string startingText = "Starting the match...";
    [SerializeField] private string ownerOnlySettingText = "Only the host can change this";
    [SerializeField] private string privateLobbyText = "Private - nobody can join";
    [SerializeField] private string publicLobbyText = "Public - anybody can join";
    [SerializeField] private string makePublicActionText = "Make public";
    [SerializeField] private string makePrivateActionText = "Make private";

    [Header("Player rows")]
    [SerializeField] private string ownerStatusText = "Owner";
    [SerializeField] private string readyStatusText = "Ready";
    [SerializeField] private string notReadyStatusText = "Not ready";
    [SerializeField] private string kickActionText = "Remove";
    [SerializeField] private string emptyRosterText = "Waiting for the room...";

    private ILobbyReadService readService;
    private int[] difficultyIds = Array.Empty<int>();
    private string[] difficultyDescriptions = Array.Empty<string>();

    private VisualElement boundRoot;
    private VisualElement screen;
    private VisualElement roster;
    private VisualElement kickPanel;
    private Label playerCountLabel;
    private Label difficultyDescriptionLabel;
    private Label visibilityLabel;
    private Label startHintLabel;
    private Label kickTextLabel;
    private DropdownField difficultyField;
    private Button visibilityButton;
    private Button readyButton;
    private Button startButton;
    private Button leaveButton;
    private Button kickConfirmButton;
    private Button kickCancelButton;

    // Nobody is removed on one click: it cannot be undone for the rest of the
    // session, and the row under the pointer moves as players come and go.
    private bool hasPendingKick;
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
        kickPanel = root.Q<VisualElement>("KickPanel");
        playerCountLabel = root.Q<Label>("PlayerCount");
        difficultyDescriptionLabel = root.Q<Label>("DifficultyDescription");
        visibilityLabel = root.Q<Label>("Visibility");
        startHintLabel = root.Q<Label>("StartHint");
        kickTextLabel = root.Q<Label>("KickText");
        difficultyField = root.Q<DropdownField>("Difficulty");
        visibilityButton = root.Q<Button>("VisibilityButton");
        readyButton = root.Q<Button>("ReadyButton");
        startButton = root.Q<Button>("StartButton");
        leaveButton = root.Q<Button>("LeaveButton");
        kickConfirmButton = root.Q<Button>("KickConfirmButton");
        kickCancelButton = root.Q<Button>("KickCancelButton");

        if (screen == null)
        {
            if (complainIfMissing)
                Debug.LogError($"{nameof(LobbyUI)} did not find 'Screen'.", this);

            return false;
        }

        PopulateDifficultyChoices();
        Subscribe();
        CancelPendingKick();

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

        if (visibilityButton != null)
            visibilityButton.clicked += HandleLobbyVisibilityToggleClicked;

        if (kickConfirmButton != null)
            kickConfirmButton.clicked += HandleKickConfirmed;

        if (kickCancelButton != null)
            kickCancelButton.clicked += CancelPendingKick;

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

        if (visibilityButton != null)
            visibilityButton.clicked -= HandleLobbyVisibilityToggleClicked;

        if (kickConfirmButton != null)
            kickConfirmButton.clicked -= HandleKickConfirmed;

        if (kickCancelButton != null)
            kickCancelButton.clicked -= CancelPendingKick;

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

    private void HandleLeaveLobbyClicked()
    {
        LeaveLobbyClicked?.Invoke();
    }

    private void HandleLobbyVisibilityToggleClicked()
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

        if (hasPendingKick && string.IsNullOrEmpty(ResolvePlayerName(pendingKickClientId)))
            CancelPendingKick();

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
                canKick && player.ClientId != localPlayer.ClientId));
        }

        // The sound binder listens for the whole document and cannot hear
        // elements that did not exist when it looked. Every other screen is
        // built once from markup and never needs to say anything; this one
        // makes a kick button per player, and a control that is silent under
        // the pointer reads as one that cannot be used.
        sounds?.Bind();
    }

    private VisualElement BuildPlayerRow(LobbyPlayerData player, bool isRoomOwner, bool canKick)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("roster__row");

        Label name = new Label(player.PlayerName.ToString());
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
        if (kickPanel == null)
        {
            PlayerKickRequested?.Invoke(clientId);
            return;
        }

        hasPendingKick = true;
        pendingKickClientId = clientId;

        if (kickTextLabel != null)
            kickTextLabel.text = string.Format(kickConfirmFormat, ResolvePlayerName(clientId));

        UiFade.Set(kickPanel, true, OverlayOpenClass);
    }

    private void HandleKickConfirmed()
    {
        if (!hasPendingKick)
            return;

        ulong clientId = pendingKickClientId;
        CancelPendingKick();
        PlayerKickRequested?.Invoke(clientId);
    }

    private void CancelPendingKick()
    {
        hasPendingKick = false;
        UiFade.Set(kickPanel, false, OverlayOpenClass);
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
                ? notReadyActionText
                : readyActionText;
        }

        RefreshStartHint(isLobbyPhaseOpen);

        // Everybody needs to know whether the door is open, not just whoever
        // can move it - otherwise a player has no way to tell why their friend
        // cannot get in.
        if (visibilityLabel != null)
        {
            visibilityLabel.text = readService.Settings.IsPublic
                ? publicLobbyText
                : privateLobbyText;
        }

        // Only the owner decides who may walk in, and only while the lobby is
        // still a lobby - once the match is starting it takes nobody anyway.
        if (visibilityButton != null)
        {
            visibilityButton.style.display = readService.IsLocalPlayerRoomOwner
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            visibilityButton.SetEnabled(isLobbyPhaseOpen);
            visibilityButton.text = readService.Settings.IsPublic
                ? makePrivateActionText
                : makePublicActionText;
        }
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

        startHintLabel.text = notReadyCount > 0
            ? string.Format(waitingForReadyFormat, notReadyCount)
            : string.Empty;
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
