public sealed class LobbyMinPlayersStartRule : ILobbyStartRule
{
    public bool CanStart(LobbyState lobbyState)
    {
        LobbySettingsData settings = lobbyState.Settings.Value;
        return lobbyState.Players.Count >= settings.MinPlayersToStart;
    }
}
