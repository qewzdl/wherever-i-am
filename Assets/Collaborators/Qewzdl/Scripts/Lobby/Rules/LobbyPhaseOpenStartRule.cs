public sealed class LobbyPhaseOpenStartRule : ILobbyStartRule
{
    public bool CanStart(LobbyState lobbyState)
    {
        return lobbyState.Phase.Value == LobbyPhase.Open;
    }
}
