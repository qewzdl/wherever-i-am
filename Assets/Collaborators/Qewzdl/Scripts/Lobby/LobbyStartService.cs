using UnityEngine;

public class LobbyStartService
{
    private readonly LobbyState lobbyState;
    private readonly LobbyStartRules startRules;

    public LobbyStartService(LobbyState lobbyState, LobbyStartRules startRules)
    {
        this.lobbyState = lobbyState;
        this.startRules = startRules;
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

        if (NetworkSessionOrchestrator.Instance == null)
        {
            Debug.LogError("NetworkSessionOrchestrator.Instance is null.");
            return;
        }

        NetworkSessionOrchestrator.Instance.StartGame();
    }

    private bool HasLobbyState()
    {
        if (lobbyState != null)
            return true;

        Debug.LogError("LobbyState is missing.");
        return false;
    }
}