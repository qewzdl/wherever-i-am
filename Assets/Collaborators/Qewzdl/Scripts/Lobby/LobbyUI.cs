using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private LobbyPlayerRow playerRowPrefab;
    [SerializeField] private Transform playerListRoot;
    [SerializeField] private Button readyButton;
    [SerializeField] private TMP_Text readyButtonLabel;
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private TMP_Text lobbyVisibilityText;
    [SerializeField] private Button lobbyVisibilityButton;
    [SerializeField] private TMP_Text lobbyVisibilityButtonLabel;
    [SerializeField] private TMP_Dropdown difficultyDropdown;
    [SerializeField] private EnemyDifficultyCatalog difficultyCatalog;
    [SerializeField] private string readyActionText = "Ready";
    [SerializeField] private string notReadyActionText = "Not ready";
    [SerializeField] private string privateLobbyText = "Private - nobody can join";
    [SerializeField] private string publicLobbyText = "Public - anybody can join";
    [SerializeField] private string makePublicActionText = "Make public";
    [SerializeField] private string makePrivateActionText = "Make private";

    private ILobbyReadService readService;
    private int[] difficultyIds = Array.Empty<int>();
    private readonly List<LobbyPlayerRow> playerRows = new List<LobbyPlayerRow>();

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
        CacheReadyButtonLabel();
        CacheLobbyVisibilityButtonLabel();
        PopulateDifficultyDropdown();

        if (difficultyDropdown != null)
            difficultyDropdown.onValueChanged.AddListener(HandleDifficultySelected);

        if (readyButton != null)
            readyButton.onClick.AddListener(HandleReadyClicked);

        if (startGameButton != null)
            startGameButton.onClick.AddListener(HandleStartGameClicked);

        if (leaveButton != null)
            leaveButton.onClick.AddListener(HandleLeaveLobbyClicked);

        if (lobbyVisibilityButton != null)
            lobbyVisibilityButton.onClick.AddListener(HandleLobbyVisibilityToggleClicked);
    }

    private void Start()
    {
        Refresh();
    }

    private void OnDestroy()
    {
        if (difficultyDropdown != null)
            difficultyDropdown.onValueChanged.RemoveListener(HandleDifficultySelected);

        if (readyButton != null)
            readyButton.onClick.RemoveListener(HandleReadyClicked);

        if (startGameButton != null)
            startGameButton.onClick.RemoveListener(HandleStartGameClicked);

        if (leaveButton != null)
            leaveButton.onClick.RemoveListener(HandleLeaveLobbyClicked);

        if (lobbyVisibilityButton != null)
            lobbyVisibilityButton.onClick.RemoveListener(HandleLobbyVisibilityToggleClicked);

        Dispose();
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

    private void HandleDifficultySelected(int optionIndex)
    {
        if (optionIndex < 0 || optionIndex >= difficultyIds.Length)
            return;

        DifficultySelected?.Invoke(difficultyIds[optionIndex]);
    }

    private void Refresh()
    {
        if (readService == null)
            return;

        RefreshPlayers();
        RefreshButtons();
        RefreshDifficulty();
    }

    private void PopulateDifficultyDropdown()
    {
        if (difficultyDropdown == null)
            return;

        if (difficultyCatalog == null)
        {
            difficultyDropdown.gameObject.SetActive(false);
            return;
        }

        int count = difficultyCatalog.Count;
        difficultyIds = new int[count];
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
            optionLabels.Add(entry.DisplayName);
        }

        difficultyDropdown.ClearOptions();
        difficultyDropdown.AddOptions(optionLabels);
    }

    // Everyone sees the choice, only the owner can move it. SetValueWithoutNotify
    // keeps the replicated value from bouncing straight back as a new command.
    private void RefreshDifficulty()
    {
        if (difficultyDropdown == null || difficultyIds.Length == 0)
            return;

        difficultyDropdown.interactable =
            readService.Phase == LobbyPhase.Open && readService.IsLocalPlayerRoomOwner;

        int selectedDifficultyId = readService.Settings.DifficultyId;

        for (int i = 0; i < difficultyIds.Length; i++)
        {
            if (difficultyIds[i] != selectedDifficultyId)
                continue;

            difficultyDropdown.SetValueWithoutNotify(i);
            difficultyDropdown.RefreshShownValue();
            return;
        }
    }

    private void RefreshPlayers()
    {
        if (playerRowPrefab == null || playerListRoot == null)
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

        EnsurePlayerRowCount(playerCount);

        for (int i = 0; i < playerRows.Count; i++)
        {
            LobbyPlayerRow row = playerRows[i];

            if (i >= playerCount)
            {
                row.gameObject.SetActive(false);
                continue;
            }

            LobbyPlayerData player = readService.GetPlayer(i);

            row.gameObject.SetActive(true);
            row.Bind(
                player,
                player.ClientId == readService.RoomOwnerClientId,
                canKick && player.ClientId != localPlayer.ClientId);
        }
    }

    // Rows are made once and reused. A lobby holds a handful of players, so
    // hiding the spare rows costs less than rebuilding the list on every change.
    private void EnsurePlayerRowCount(int playerCount)
    {
        while (playerRows.Count < playerCount)
        {
            LobbyPlayerRow row = Instantiate(playerRowPrefab, playerListRoot);

            row.KickClicked += HandlePlayerKickRequested;
            playerRows.Add(row);
        }
    }

    private void HandlePlayerKickRequested(ulong clientId)
    {
        PlayerKickRequested?.Invoke(clientId);
    }

    private void RefreshButtons()
    {
        bool isLobbyPhaseOpen = readService.Phase == LobbyPhase.Open;

        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(readService.IsLocalPlayerRoomOwner);
            startGameButton.interactable = isLobbyPhaseOpen && readService.CanStartGame;
        }

        if (readyButton != null)
        {
            bool hasLocalPlayer = readService.TryGetLocalPlayer(out LobbyPlayerData localPlayer);

            readyButton.interactable = isLobbyPhaseOpen && hasLocalPlayer;
            SetReadyButtonLabel(hasLocalPlayer && localPlayer.IsReady);
        }

        // Everybody needs to know whether the door is open, not just whoever
        // can move it - otherwise a player has no way to tell why their friend
        // cannot get in.
        if (lobbyVisibilityText != null)
        {
            lobbyVisibilityText.text = readService.Settings.IsPublic
                ? publicLobbyText
                : privateLobbyText;
        }

        // Only the owner decides who may walk in, and only while the lobby is
        // still a lobby - once the match is starting it takes nobody anyway.
        if (lobbyVisibilityButton != null)
        {
            lobbyVisibilityButton.gameObject.SetActive(readService.IsLocalPlayerRoomOwner);
            lobbyVisibilityButton.interactable = isLobbyPhaseOpen;
            SetLobbyVisibilityButtonLabel(readService.Settings.IsPublic);
        }
    }

    private void CacheLobbyVisibilityButtonLabel()
    {
        if (lobbyVisibilityButtonLabel != null || lobbyVisibilityButton == null)
            return;

        lobbyVisibilityButtonLabel =
            lobbyVisibilityButton.GetComponentInChildren<TMP_Text>(true);
    }

    private void SetLobbyVisibilityButtonLabel(bool isPublic)
    {
        CacheLobbyVisibilityButtonLabel();

        if (lobbyVisibilityButtonLabel == null)
            return;

        lobbyVisibilityButtonLabel.text =
            isPublic ? makePrivateActionText : makePublicActionText;
    }

    private void CacheReadyButtonLabel()
    {
        if (readyButtonLabel != null || readyButton == null)
            return;

        readyButtonLabel = readyButton.GetComponentInChildren<TMP_Text>(true);
    }

    private void SetReadyButtonLabel(bool isReady)
    {
        CacheReadyButtonLabel();

        if (readyButtonLabel == null)
            return;

        readyButtonLabel.text = isReady ? notReadyActionText : readyActionText;
    }
}
