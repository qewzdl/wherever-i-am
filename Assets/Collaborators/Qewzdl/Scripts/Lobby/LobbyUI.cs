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

    private ILobbyService lobbyService;
    private bool isReady;

    public void Construct(ILobbyService service)
    {
        if (lobbyService != null)
            lobbyService.LobbyChanged -= Refresh;

        lobbyService = service;

        if (lobbyService != null)
            lobbyService.LobbyChanged += Refresh;

        Refresh();
    }

    private void Awake()
    {
        if (readyButton != null) readyButton.onClick.AddListener(ToggleReady);

        if (startGameButton != null) startGameButton.onClick.AddListener(StartGame);

        if (leaveButton != null) leaveButton.onClick.AddListener(LeaveLobby);
    }

    private void Start()
    {
        Refresh();
    }

    private void OnDestroy()
    {
        if (readyButton != null) readyButton.onClick.RemoveListener(ToggleReady);

        if (startGameButton != null) startGameButton.onClick.RemoveListener(StartGame);

        if (leaveButton != null) leaveButton.onClick.RemoveListener(LeaveLobby);

        if (lobbyService != null) lobbyService.LobbyChanged -= Refresh;
    }

    private void ToggleReady()
    {
        if (lobbyService == null)
        {
            Debug.LogError("Lobby service is not assigned.");
            return;
        }

        isReady = !isReady;
        lobbyService.SetReady(isReady);
    }

    private void StartGame()
    {
        if (lobbyService == null)
        {
            Debug.LogError("Lobby service is not assigned.");
            return;
        }

        lobbyService.StartGame();
    }

    private void LeaveLobby()
    {
        if (lobbyService == null)
        {
            Debug.LogError("Lobby service is not assigned.");
            return;
        }

        lobbyService.LeaveLobby();
    }

    private void Refresh()
    {
        if (lobbyService == null)
            return;

        RefreshPlayers();
        RefreshButtons();
    }

    private void RefreshPlayers()
    {
        if (playersText == null)
            return;

        StringBuilder builder = new StringBuilder();

        for (int i = 0; i < lobbyService.PlayerCount; i++)
        {
            LobbyPlayerData player = lobbyService.GetPlayer(i);

            string hostText = player.IsHost ? "Host" : "Client";
            string readyText = player.IsReady ? "Ready" : "Not ready";

            builder.AppendLine($"{player.PlayerName} | {hostText} | {readyText} | Character {player.CharacterId}");
        }

        playersText.text = builder.ToString();
    }

    private void RefreshButtons()
    {
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(lobbyService.IsHost);
            startGameButton.interactable = lobbyService.CanStartGame;
        }
    }
}