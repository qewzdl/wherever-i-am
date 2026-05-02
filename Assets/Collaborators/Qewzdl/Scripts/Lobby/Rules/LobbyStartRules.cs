public class LobbyStartRules
{
    private readonly ILobbyStartRule[] rules;

    public LobbyStartRules() : this(
        new LobbyPhaseOpenStartRule(),
        new LobbyMinPlayersStartRule(),
        new LobbyAllPlayersReadyStartRule())
    {
    }

    public LobbyStartRules(params ILobbyStartRule[] rules)
    {
        this.rules = rules ?? new ILobbyStartRule[0];
    }

    public bool CanStart(LobbyState lobbyState)
    {
        if (lobbyState == null || lobbyState.Players == null)
            return false;

        for (int i = 0; i < rules.Length; i++)
        {
            ILobbyStartRule rule = rules[i];

            if (rule != null && !rule.CanStart(lobbyState))
                return false;
        }

        return true;
    }
}
