public class LobbyStartRules
{
    public bool CanStart(LobbyState lobbyState)
    {
        if (lobbyState == null || lobbyState.Players == null)
            return false;

        LobbySettingsData settings = lobbyState.Settings.Value;

        if (lobbyState.Players.Count < settings.MinPlayersToStart)
            return false;

        if (lobbyState.Players.Count > settings.MaxPlayers)
            return false;

        if (!settings.RequireAllPlayersReady)
            return true;

        for (int i = 0; i < lobbyState.Players.Count; i++)
        {
            if (!lobbyState.Players[i].IsReady)
                return false;
        }

        return true;
    }
}