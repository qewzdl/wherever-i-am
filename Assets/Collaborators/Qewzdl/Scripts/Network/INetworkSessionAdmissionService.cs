public interface INetworkSessionAdmissionService
{
    bool TryGetPlayerId(ulong clientId, out string playerId);
    bool IsReconnect(ulong clientId);
    bool HasReconnectReservation(string playerId);
    void RecordDisconnect(ulong clientId);
}
