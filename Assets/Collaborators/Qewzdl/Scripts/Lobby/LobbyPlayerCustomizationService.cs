using UnityEngine;

public class LobbyPlayerCustomizationService
{
    private readonly LobbyState lobbyState;

    public LobbyPlayerCustomizationService(LobbyState lobbyState)
    {
        this.lobbyState = lobbyState;
    }

    public void SetReady(ulong clientId, bool isReady)
    {
        if (!TryGetPlayer(clientId, out int index, out LobbyPlayerData player))
            return;

        player.IsReady = isReady;
        lobbyState.Players[index] = player;
    }

    // Ready means "I agree to start this match". Change what the match is and
    // nobody has agreed to the new one, so the whole room stands down and says
    // so again - which is also the only way the host finds out that somebody
    // minded.
    public void ClearAllReady()
    {
        if (lobbyState == null)
        {
            Debug.LogError("LobbyState is missing.");
            return;
        }

        for (int i = 0; i < lobbyState.Players.Count; i++)
        {
            LobbyPlayerData player = lobbyState.Players[i];

            if (!player.IsReady)
                continue;

            player.IsReady = false;
            lobbyState.Players[i] = player;
        }
    }

    private bool TryGetPlayer(ulong clientId, out int index, out LobbyPlayerData player)
    {
        index = -1;
        player = default;

        if (lobbyState == null)
        {
            Debug.LogError("LobbyState is missing.");
            return false;
        }

        if (!lobbyState.TryGetPlayerIndex(clientId, out index))
            return false;

        player = lobbyState.Players[index];
        return true;
    }
}
