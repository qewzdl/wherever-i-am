using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text playersText;
    [SerializeField] private Button readyButton;
    [SerializeField] private TMP_Text readyButtonLabel;
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private Button lobbyVisibilityButton;
    [SerializeField] private TMP_Text lobbyVisibilityButtonLabel;
    [SerializeField] private TMP_Dropdown difficultyDropdown;
    [SerializeField] private EnemyDifficultyCatalog difficultyCatalog;
    [SerializeField] private string readyActionText = "Ready";
    [SerializeField] private string notReadyActionText = "Not ready";
    [SerializeField] private string makePublicActionText = "Make public";
    [SerializeField] private string makePrivateActionText = "Make private";

    private ILobbyReadService readService;
    private int[] difficultyIds = Array.Empty<int>();

    public event Action ReadyClicked;
    public event Action StartGameClicked;
    public event Action LeaveLobbyClicked;
    public event Action<int> DifficultySelected;
    public event Action LobbyVisibilityToggleClicked;

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
        if (playersText == null)
            return;

        StringBuilder builder = new StringBuilder();

        builder.AppendLine($"Lobby phase: {readService.Phase}");
        builder.AppendLine();

        for (int i = 0; i < readService.PlayerCount; i++)
        {
            LobbyPlayerData player = readService.GetPlayer(i);

            string ownerText = player.ClientId == readService.RoomOwnerClientId ? "Owner" : "Player";
            string readyText = player.IsReady ? "Ready" : "Not ready";

            builder.AppendLine($"{player.PlayerName} | {ownerText} | {readyText}");
        }

        playersText.text = builder.ToString();
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
