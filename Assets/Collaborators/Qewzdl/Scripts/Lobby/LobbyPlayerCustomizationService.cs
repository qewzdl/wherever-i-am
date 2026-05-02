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

    public void SetCharacter(ulong clientId, int characterId)
    {
        if (!TryGetPlayer(clientId, out int index, out LobbyPlayerData player))
            return;

        player.CharacterId = characterId;
        lobbyState.Players[index] = player;
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