public class LobbyStartRules
{
    private readonly LobbyConfig config;

    public LobbyStartRules(LobbyConfig config)
    {
        this.config = config;
    }

    public bool CanStart(LobbyState lobbyState)
    {
        if (lobbyState == null || lobbyState.Players == null)
            return false;

        if (config == null)
            return false;

        if (lobbyState.Players.Count < config.MinPlayersToStart)
            return false;

        if (lobbyState.Players.Count > config.MaxPlayers)
            return false;

        if (!config.RequireAllPlayersReady)
            return true;

        for (int i = 0; i < lobbyState.Players.Count; i++)
        {
            if (!lobbyState.Players[i].IsReady)
                return false;
        }

        return true;
    }
}