public sealed class LobbyAllPlayersReadyStartRule : ILobbyStartRule
{
    public bool CanStart(LobbyState lobbyState)
    {
        LobbySettingsData settings = lobbyState.Settings.Value;

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
