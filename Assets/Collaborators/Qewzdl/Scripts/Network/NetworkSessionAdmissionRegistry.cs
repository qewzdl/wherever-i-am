using System;
using System.Collections.Generic;

internal enum NetworkAdmissionStatus
{
    Accepted,
    Reconnected,
    ProtocolMismatch,
    BuildMismatch,
    DuplicatePlayer,
    SessionFull
}

internal readonly struct NetworkAdmissionResult
{
    internal NetworkAdmissionStatus Status { get; }
    internal bool Accepted =>
        Status == NetworkAdmissionStatus.Accepted ||
        Status == NetworkAdmissionStatus.Reconnected;
    internal bool IsReconnect => Status == NetworkAdmissionStatus.Reconnected;

    internal NetworkAdmissionResult(NetworkAdmissionStatus status)
    {
        Status = status;
    }
}

internal sealed class NetworkSessionAdmissionRegistry
{
    private readonly int maxPlayers;
    private readonly ushort protocolVersion;
    private readonly string buildVersion;
    private readonly double reconnectGracePeriodSeconds;
    private readonly Func<double> timeProvider;

    // Asks the transport whether a client is still there. The registry's own
    // bookkeeping only learns about a drop when the disconnect callback fires,
    // and a timed out connection can outlive the player by seconds. Null means
    // there is nothing to ask, and the bookkeeping is taken at its word.
    private readonly Func<ulong, bool> isClientStillConnected;
    private readonly Dictionary<string, PlayerRecord> records =
        new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, string> activeClientIds = new();
    private readonly Dictionary<ulong, RecentClientRecord> recentClientIds = new();
    private readonly List<string> expiredPlayerIds = new();
    private readonly List<ulong> expiredClientIds = new();

    internal int ActivePlayerCount
    {
        get
        {
            PurgeExpired();
            return activeClientIds.Count;
        }
    }

    internal int ReservedPlayerCount
    {
        get
        {
            PurgeExpired();
            return records.Count - activeClientIds.Count;
        }
    }

    internal NetworkSessionAdmissionRegistry(
        int maxPlayers,
        ushort protocolVersion,
        string buildVersion,
        double reconnectGracePeriodSeconds,
        Func<double> timeProvider,
        Func<ulong, bool> isClientStillConnected = null)
    {
        if (maxPlayers < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPlayers));

        if (protocolVersion == 0)
            throw new ArgumentOutOfRangeException(nameof(protocolVersion));

        if (string.IsNullOrWhiteSpace(buildVersion))
            throw new ArgumentException("Build version is required.", nameof(buildVersion));

        if (reconnectGracePeriodSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reconnectGracePeriodSeconds));
        }

        this.maxPlayers = maxPlayers;
        this.protocolVersion = protocolVersion;
        this.buildVersion = buildVersion.Trim();
        this.reconnectGracePeriodSeconds = reconnectGracePeriodSeconds;
        this.timeProvider = timeProvider ??
                            throw new ArgumentNullException(nameof(timeProvider));
        this.isClientStillConnected = isClientStillConnected;
    }

    internal NetworkAdmissionResult TryAdmit(
        ulong clientId,
        NetworkConnectionPayload payload)
    {
        PurgeExpired();

        if (payload.ProtocolVersion != protocolVersion)
        {
            return new NetworkAdmissionResult(
                NetworkAdmissionStatus.ProtocolMismatch);
        }

        if (!string.Equals(
                payload.BuildVersion,
                buildVersion,
                StringComparison.Ordinal))
        {
            return new NetworkAdmissionResult(
                NetworkAdmissionStatus.BuildMismatch);
        }

        if (records.TryGetValue(payload.PlayerId, out PlayerRecord existing))
        {
            if (existing.IsActive)
            {
                if (existing.ClientId == clientId)
                {
                    return new NetworkAdmissionResult(existing.IsReconnect
                        ? NetworkAdmissionStatus.Reconnected
                        : NetworkAdmissionStatus.Accepted);
                }

                // Two copies of the same player really are running, so the
                // second one is refused.
                if (IsStillConnected(existing.ClientId))
                {
                    return new NetworkAdmissionResult(
                        NetworkAdmissionStatus.DuplicatePlayer);
                }

                // The old connection is gone and nobody has told us yet. This
                // is the player coming back, not a second copy - refusing here
                // would lock them out of their own session until the transport
                // caught up.
                ReleaseActiveClient(existing.ClientId);
            }

            existing.IsActive = true;
            existing.IsReconnect = true;
            existing.ClientId = clientId;
            existing.ReservationExpiresAt = 0d;
            activeClientIds[clientId] = payload.PlayerId;
            recentClientIds.Remove(clientId);
            return new NetworkAdmissionResult(NetworkAdmissionStatus.Reconnected);
        }

        if (records.Count >= maxPlayers)
        {
            return new NetworkAdmissionResult(NetworkAdmissionStatus.SessionFull);
        }

        records.Add(
            payload.PlayerId,
            new PlayerRecord(clientId, isReconnect: false));
        activeClientIds[clientId] = payload.PlayerId;
        recentClientIds.Remove(clientId);
        return new NetworkAdmissionResult(NetworkAdmissionStatus.Accepted);
    }

    private bool IsStillConnected(ulong clientId)
    {
        return isClientStillConnected == null ||
               isClientStillConnected.Invoke(clientId);
    }

    private void ReleaseActiveClient(ulong clientId)
    {
        activeClientIds.Remove(clientId);
        recentClientIds.Remove(clientId);
    }

    internal void RecordDisconnect(ulong clientId, bool reserveSlot)
    {
        PurgeExpired();

        if (!activeClientIds.TryGetValue(clientId, out string playerId) ||
            !records.TryGetValue(playerId, out PlayerRecord record) ||
            !record.IsActive ||
            record.ClientId != clientId)
        {
            return;
        }

        activeClientIds.Remove(clientId);

        if (!reserveSlot || reconnectGracePeriodSeconds <= 0d)
        {
            records.Remove(playerId);
            recentClientIds.Remove(clientId);
            return;
        }

        double expiresAt = timeProvider.Invoke() + reconnectGracePeriodSeconds;
        record.IsActive = false;
        record.IsReconnect = false;
        record.ReservationExpiresAt = expiresAt;
        recentClientIds[clientId] = new RecentClientRecord(playerId, expiresAt);
    }

    internal bool TryGetPlayerId(ulong clientId, out string playerId)
    {
        PurgeExpired();

        if (activeClientIds.TryGetValue(clientId, out playerId))
            return true;

        if (recentClientIds.TryGetValue(
                clientId,
                out RecentClientRecord recent))
        {
            playerId = recent.PlayerId;
            return true;
        }

        playerId = string.Empty;
        return false;
    }

    internal bool IsReconnect(ulong clientId)
    {
        PurgeExpired();

        return activeClientIds.TryGetValue(clientId, out string playerId) &&
               records.TryGetValue(playerId, out PlayerRecord record) &&
               record.IsActive &&
               record.ClientId == clientId &&
               record.IsReconnect;
    }

    internal bool HasReconnectReservation(string playerId)
    {
        PurgeExpired();

        return !string.IsNullOrEmpty(playerId) &&
               records.TryGetValue(playerId, out PlayerRecord record) &&
               !record.IsActive;
    }

    internal void Reset()
    {
        records.Clear();
        activeClientIds.Clear();
        recentClientIds.Clear();
        expiredPlayerIds.Clear();
        expiredClientIds.Clear();
    }

    private void PurgeExpired()
    {
        double now = timeProvider.Invoke();
        expiredPlayerIds.Clear();

        foreach (KeyValuePair<string, PlayerRecord> pair in records)
        {
            if (!pair.Value.IsActive && pair.Value.ReservationExpiresAt <= now)
                expiredPlayerIds.Add(pair.Key);
        }

        for (int i = 0; i < expiredPlayerIds.Count; i++)
            records.Remove(expiredPlayerIds[i]);

        expiredClientIds.Clear();

        foreach (KeyValuePair<ulong, RecentClientRecord> pair in recentClientIds)
        {
            if (pair.Value.ExpiresAt <= now ||
                !records.ContainsKey(pair.Value.PlayerId))
            {
                expiredClientIds.Add(pair.Key);
            }
        }

        for (int i = 0; i < expiredClientIds.Count; i++)
            recentClientIds.Remove(expiredClientIds[i]);
    }

    private sealed class PlayerRecord
    {
        internal ulong ClientId;
        internal bool IsActive;
        internal bool IsReconnect;
        internal double ReservationExpiresAt;

        internal PlayerRecord(ulong clientId, bool isReconnect)
        {
            ClientId = clientId;
            IsActive = true;
            IsReconnect = isReconnect;
        }
    }

    private readonly struct RecentClientRecord
    {
        internal string PlayerId { get; }
        internal double ExpiresAt { get; }

        internal RecentClientRecord(string playerId, double expiresAt)
        {
            PlayerId = playerId;
            ExpiresAt = expiresAt;
        }
    }
}
