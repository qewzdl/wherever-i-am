using UnityEngine;

public class LobbyPlayerCustomizationService
{
    private readonly LobbyState lobbyState;
    private readonly LobbyConfig lobbyConfig;

    public LobbyPlayerCustomizationService(LobbyState lobbyState, LobbyConfig lobbyConfig)
    {
        this.lobbyState = lobbyState;
        this.lobbyConfig = lobbyConfig;
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

        if (!IsValidCharacterId(characterId))
            return;

        player.CharacterId = characterId;
        lobbyState.Players[index] = player;
    }

    private bool IsValidCharacterId(int characterId)
    {
        if (lobbyConfig != null && lobbyConfig.IsValidCharacterId(characterId))
            return true;

        Debug.LogWarning($"Rejected invalid lobby character id: {characterId}.");
        return false;
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
