public interface INetworkSessionAdmissionService
{
    bool TryGetPlayerId(ulong clientId, out string playerId);
    bool TryGetPlayerName(ulong clientId, out string playerName);
    bool IsReconnect(ulong clientId);
    bool HasReconnectReservation(string playerId);
    void RecordDisconnect(ulong clientId);
    void SetAcceptingNewPlayers(bool accepting);
    bool KickPlayer(ulong clientId);
    bool WasKicked(ulong clientId);
}
