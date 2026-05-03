using UnityEngine;

public class LobbyStartService
{
    private readonly LobbyState lobbyState;
    private readonly LobbyStartRules startRules;
    private readonly INetworkSessionService sessionService;

    public LobbyStartService(
        LobbyState lobbyState,
        LobbyStartRules startRules,
        INetworkSessionService sessionService)
    {
        this.lobbyState = lobbyState;
        this.startRules = startRules;
        this.sessionService = sessionService;
    }

    public bool CanStartGame()
    {
        if (!HasLobbyState())
            return false;

        return lobbyState.CanStartGame.Value;
    }

    public void RefreshCanStartGame()
    {
        if (!HasLobbyState())
            return;

        lobbyState.CanStartGame.Value = startRules != null && startRules.CanStart(lobbyState);
    }

    public void TryStartGame()
    {
        RefreshCanStartGame();

        if (!CanStartGame())
            return;

        if (sessionService == null)
        {
            Debug.LogError("Network session service is missing.");
            return;
        }

        lobbyState.Phase.Value = LobbyPhase.Starting;
        lobbyState.CanStartGame.Value = false;

        sessionService.StartGame();
    }

    private bool HasLobbyState()
    {
        if (lobbyState != null)
            return true;

        Debug.LogError("LobbyState is missing.");
        return false;
    }
}