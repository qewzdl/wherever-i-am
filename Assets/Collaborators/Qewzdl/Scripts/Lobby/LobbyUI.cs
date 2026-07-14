using System;
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
    [SerializeField] private string readyActionText = "Ready";
    [SerializeField] private string notReadyActionText = "Not ready";

    private ILobbyReadService readService;

    public event Action ReadyClicked;
    public event Action StartGameClicked;
    public event Action LeaveLobbyClicked;

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

        if (readyButton != null)
            readyButton.onClick.AddListener(HandleReadyClicked);

        if (startGameButton != null)
            startGameButton.onClick.AddListener(HandleStartGameClicked);

        if (leaveButton != null)
            leaveButton.onClick.AddListener(HandleLeaveLobbyClicked);
    }

    private void Start()
    {
        Refresh();
    }

    private void OnDestroy()
    {
        if (readyButton != null)
            readyButton.onClick.RemoveListener(HandleReadyClicked);

        if (startGameButton != null)
            startGameButton.onClick.RemoveListener(HandleStartGameClicked);

        if (leaveButton != null)
            leaveButton.onClick.RemoveListener(HandleLeaveLobbyClicked);

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

    private void Refresh()
    {
        if (readService == null)
            return;

        RefreshPlayers();
        RefreshButtons();
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
        bool isLobbyOpen = readService.Phase == LobbyPhase.Open;

        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(readService.IsLocalPlayerRoomOwner);
            startGameButton.interactable = isLobbyOpen && readService.CanStartGame;
        }

        if (readyButton != null)
        {
            bool hasLocalPlayer = readService.TryGetLocalPlayer(out LobbyPlayerData localPlayer);

            readyButton.interactable = isLobbyOpen && hasLocalPlayer;
            SetReadyButtonLabel(hasLocalPlayer && localPlayer.IsReady);
        }
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
