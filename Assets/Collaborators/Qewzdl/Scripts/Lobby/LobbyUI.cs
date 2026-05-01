using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text playersText;
    [SerializeField] private Button readyButton;
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button leaveButton;

    private ILobbyReadService readService;
    private ILobbyCommandService commandService;

    private bool isReady;

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

        isReady = !isReady;
        commandService.SetReady(isReady);
    }

    private void StartGame()
    {
        if (commandService == null)
        {
            Debug.LogError("Lobby command service is not assigned.");
            return;
        }

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

        for (int i = 0; i < readService.PlayerCount; i++)
        {
            LobbyPlayerData player = readService.GetPlayer(i);

            string ownerText = player.IsRoomOwner ? "Owner" : "Player";
            string readyText = player.IsReady ? "Ready" : "Not ready";

            builder.AppendLine($"{player.PlayerName} | {ownerText} | {readyText} | Character {player.CharacterId}");
        }

        playersText.text = builder.ToString();
    }

    private void RefreshButtons()
    {
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(readService.IsLocalPlayerRoomOwner);
            startGameButton.interactable = readService.CanStartGame;
        }
    }
}