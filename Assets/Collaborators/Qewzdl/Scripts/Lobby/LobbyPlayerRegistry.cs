using UnityEngine;

public class LobbyPlayerRegistry
{
    private readonly LobbyState lobbyState;
    private readonly LobbyOwnershipService ownershipService;

    public LobbyPlayerRegistry(
        LobbyState lobbyState,
        LobbyOwnershipService ownershipService)
    {
        this.lobbyState = lobbyState;
        this.ownershipService = ownershipService;
    }

    public bool TryAddPlayer(ulong clientId)
    {
        if (!HasLobbyState())
            return false;

        if (lobbyState.TryGetPlayerIndex(clientId, out _))
            return true;

        if (!CanAcceptNewPlayer())
            return false;

        bool shouldBecomeRoomOwner = !ownershipService.HasValidRoomOwner();

        lobbyState.Players.Add(new LobbyPlayerData(
            clientId,
            $"Player {clientId}",
            false
        ));

        if (shouldBecomeRoomOwner)
            ownershipService.AssignRoomOwner(clientId);

        return true;
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

    private bool CanAcceptNewPlayer()
    {
        if (lobbyState.Phase.Value != LobbyPhase.Open)
        {
            Debug.LogWarning($"Rejected lobby player because lobby phase is {lobbyState.Phase.Value}.");
            return false;
        }

        int maxPlayers = lobbyState.Settings.Value.MaxPlayers;

        if (lobbyState.Players.Count < maxPlayers)
            return true;

        Debug.LogWarning($"Rejected lobby player because lobby is full: {lobbyState.Players.Count}/{maxPlayers}.");
        return false;
    }
}
