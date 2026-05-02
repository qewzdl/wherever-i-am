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
    private ILobbyCommandService commandService;

    public void Construct(ILobbyReadService readService, ILobbyCommandService commandService)
    {
        if (this.readService != null)
            this.readService.LobbyChanged -= Refresh;

        this.readService = readService;
        this.commandService = commandService;

        if (this.readService != null)
            this.readService.LobbyChanged += Refresh;

        Refresh();
    }

    private void Awake()
    {
        CacheReadyButtonLabel();

        if (readyButton != null)
            readyButton.onClick.AddListener(ToggleReady);

        if (startGameButton != null)
            startGameButton.onClick.AddListener(StartGame);

        if (leaveButton != null)
            leaveButton.onClick.AddListener(LeaveLobby);
    }

    private void Start()
    {
        Refresh();
    }

    private void OnDestroy()
    {
        if (readyButton != null)
            readyButton.onClick.RemoveListener(ToggleReady);

        if (startGameButton != null)
            startGameButton.onClick.RemoveListener(StartGame);

        if (leaveButton != null)
            leaveButton.onClick.RemoveListener(LeaveLobby);

        if (readService != null)
            readService.LobbyChanged -= Refresh;
    }

    private void ToggleReady()
    {
        if (commandService == null)
        {
            Debug.LogError("Lobby command service is not assigned.");
            return;
        }

        if (readService == null)
        {
            Debug.LogError("Lobby read service is not assigned.");
            return;
        }

        if (readService.Phase != LobbyPhase.Open)
            return;

        if (!readService.TryGetLocalPlayer(out LobbyPlayerData localPlayer))
        {
            Debug.LogWarning("Local lobby player was not found.");
            return;
        }

        bool newReadyState = !localPlayer.IsReady;
        SetReadyButtonLabel(newReadyState);
        commandService.SetReady(newReadyState);
    }

    private void StartGame()
    {
        if (commandService == null)
        {
            Debug.LogError("Lobby command service is not assigned.");
            return;
        }

        if (readService == null || readService.Phase != LobbyPhase.Open)
            return;

        commandService.StartGame();
    }

    private void LeaveLobby()
    {
        if (commandService == null)
        {
            Debug.LogError("Lobby command service is not assigned.");
            return;
        }

        commandService.LeaveLobby();
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
