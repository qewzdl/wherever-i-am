using UnityEngine;

public class LobbyPlayerRegistry
{
    private readonly LobbyState lobbyState;
    private readonly LobbyOwnershipService ownershipService;

    public LobbyPlayerRegistry(LobbyState lobbyState, LobbyOwnershipService ownershipService)
    {
        this.lobbyState = lobbyState;
        this.ownershipService = ownershipService;
    }

    public void AddPlayerIfNotExists(ulong clientId)
    {
        if (!HasLobbyState())
            return;

        if (lobbyState.TryGetPlayerIndex(clientId, out _))
            return;

        bool shouldBecomeRoomOwner = !ownershipService.HasValidRoomOwner();

        lobbyState.Players.Add(new LobbyPlayerData(
            clientId,
            $"Player {clientId}",
            false,
            shouldBecomeRoomOwner,
            0
        ));

        if (shouldBecomeRoomOwner)
            ownershipService.AssignRoomOwner(clientId);
    }

    public void RemovePlayer(ulong clientId)
    {
        if (!HasLobbyState())
            return;

        if (!lobbyState.TryGetPlayerIndex(clientId, out int index))
            return;

        bool wasRoomOwner = ownershipService.IsRoomOwner(clientId);

        lobbyState.Players.RemoveAt(index);

        if (wasRoomOwner)
            ownershipService.AssignNextRoomOwner();
    }

    private bool HasLobbyState()
    {
        if (lobbyState != null)
            return true;

        Debug.LogError("LobbyState is missing.");
        return false;
    }
}