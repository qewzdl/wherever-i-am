public interface INetworkSessionAdmissionService
{
    bool TryGetPlayerId(ulong clientId, out string playerId);
    bool TryGetPlayerName(ulong clientId, out string playerName);
    bool IsReconnect(ulong clientId);
    bool HasReconnectReservation(string playerId);
    void RecordDisconnect(ulong clientId);
    void SetAcceptingNewPlayers(bool accepting);

    // Whether the door is open, as the thing that turns people away sees it.
    // This survives a match - it is Bootstrap's, not the Lobby scene's - which
    // makes it the only copy of the host's choice still standing when the
    // lobby comes back.
    bool IsAcceptingNewPlayers { get; }
    bool KickPlayer(ulong clientId);
    bool WasKicked(ulong clientId);

    // Whether a seat is held for whoever drops right now. Asked instead of
    // "is there a reservation yet", because the order in which disconnect
    // handlers run is not defined and the answer must not depend on it.
    bool HoldsSeatsForDisconnects { get; }
}
