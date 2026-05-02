using UnityEngine;

public class LobbyOwnershipService
{
    private readonly LobbyState lobbyState;

    public LobbyOwnershipService(LobbyState lobbyState)
    {
        this.lobbyState = lobbyState;
    }

    public bool IsRoomOwner(ulong clientId)
    {
        return lobbyState != null && lobbyState.RoomOwnerClientId.Value == clientId;
    }

    public bool HasValidRoomOwner()
    {
        return lobbyState != null &&
               lobbyState.RoomOwnerClientId.Value != LobbyState.NoRoomOwner &&
               lobbyState.TryGetPlayerIndex(lobbyState.RoomOwnerClientId.Value, out _);
    }

    public bool CanChangeSettings(ulong clientId)
    {
        if (IsRoomOwner(clientId))
            return true;

        Debug.LogWarning("Only room owner can change lobby settings.");
        return false;
    }

    public bool CanStartGame(ulong clientId)
    {
        if (IsRoomOwner(clientId))
            return true;

        Debug.LogWarning("Only room owner can request game start.");
        return false;
    }

    public void AssignRoomOwner(ulong clientId)
    {
        if (!HasLobbyState())
            return;

        lobbyState.RoomOwnerClientId.Value = clientId;

        for (int i = 0; i < lobbyState.Players.Count; i++)
        {
            LobbyPlayerData player = lobbyState.Players[i];
            player.IsRoomOwner = player.ClientId == clientId;
            lobbyState.Players[i] = player;
        }
    }

    public void AssignNextRoomOwner()
    {
        if (!HasLobbyState())
            return;

        if (lobbyState.Players.Count == 0)
        {
            lobbyState.RoomOwnerClientId.Value = LobbyState.NoRoomOwner;
            return;
        }

        AssignRoomOwner(lobbyState.Players[0].ClientId);
    }

    private bool HasLobbyState()
    {
        if (lobbyState != null)
            return true;

        Debug.LogError("LobbyState is missing.");
        return false;
    }
}