using System;
using System.Collections.Generic;
using UnityEngine;

public class LobbyPlayerRegistry
{
    private readonly LobbyState lobbyState;
    private readonly LobbyOwnershipService ownershipService;
    private readonly INetworkSessionAdmissionService admissionService;
    private readonly Dictionary<string, LobbyPlayerSnapshot> reconnectSnapshots =
        new(StringComparer.Ordinal);
    private readonly List<string> expiredSnapshotIds = new();

    public LobbyPlayerRegistry(
        LobbyState lobbyState,
        LobbyOwnershipService ownershipService,
        INetworkSessionAdmissionService admissionService)
    {
        this.lobbyState = lobbyState;
        this.ownershipService = ownershipService;
        this.admissionService = admissionService ??
                                throw new ArgumentNullException(
                                    nameof(admissionService));
    }

    public bool TryAddPlayer(ulong clientId)
    {
        if (!HasLobbyState())
            return false;

        if (lobbyState.TryGetPlayerIndex(clientId, out _))
            return true;

        if (!CanRegisterPlayer())
            return false;

        if (!admissionService.TryGetPlayerId(clientId, out string playerId))
        {
            Debug.LogWarning(
                $"Rejected lobby registration for unadmitted client " +
                $"'{clientId}'.");
            return false;
        }

        PurgeExpiredSnapshots(playerId);
        LobbyPlayerData player = CreatePlayerData(clientId, playerId);
        bool shouldBecomeRoomOwner = !ownershipService.HasValidRoomOwner();

        lobbyState.Players.Add(player);

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

        LobbyPlayerData player = lobbyState.Players[index];
        CaptureReconnectSnapshot(clientId, player);
        bool wasRoomOwner = ownershipService.IsRoomOwner(clientId);

        lobbyState.Players.RemoveAt(index);

        if (wasRoomOwner)
            ownershipService.AssignNextRoomOwner();
    }

    private LobbyPlayerData CreatePlayerData(
        ulong clientId,
        string playerId)
    {
        if (admissionService.IsReconnect(clientId) &&
            !string.IsNullOrEmpty(playerId) &&
            reconnectSnapshots.TryGetValue(
                playerId,
                out LobbyPlayerSnapshot snapshot))
        {
            reconnectSnapshots.Remove(playerId);
            return new LobbyPlayerData(
                clientId,
                snapshot.PlayerName,
                snapshot.IsReady);
        }

        if (!string.IsNullOrEmpty(playerId))
            reconnectSnapshots.Remove(playerId);

        return new LobbyPlayerData(
            clientId,
            $"Player {clientId}",
            false);
    }

    private void CaptureReconnectSnapshot(
        ulong clientId,
        LobbyPlayerData player)
    {
        if (!admissionService.TryGetPlayerId(clientId, out string playerId) ||
            !admissionService.HasReconnectReservation(playerId))
        {
            return;
        }

        reconnectSnapshots[playerId] = new LobbyPlayerSnapshot(
            player.PlayerName.ToString(),
            player.IsReady);
    }

    private void PurgeExpiredSnapshots(string reconnectingPlayerId)
    {
        if (reconnectSnapshots.Count == 0)
            return;

        expiredSnapshotIds.Clear();

        foreach (KeyValuePair<string, LobbyPlayerSnapshot> pair in reconnectSnapshots)
        {
            if (pair.Key == reconnectingPlayerId)
                continue;

            if (!admissionService.HasReconnectReservation(pair.Key))
                expiredSnapshotIds.Add(pair.Key);
        }

        for (int i = 0; i < expiredSnapshotIds.Count; i++)
            reconnectSnapshots.Remove(expiredSnapshotIds[i]);
    }

    private bool HasLobbyState()
    {
        if (lobbyState != null)
            return true;

        Debug.LogError("LobbyState is missing.");
        return false;
    }

    private bool CanRegisterPlayer()
    {
        if (lobbyState.Phase.Value != LobbyPhase.Open)
        {
            Debug.LogWarning(
                $"Rejected lobby player because lobby phase is " +
                $"{lobbyState.Phase.Value}.");
            return false;
        }

        // Capacity belongs to the admission service, which turns a client away
        // during approval - before the connection exists. Checking it again
        // here could only ever refuse somebody already in the session, which is
        // the behaviour this replaced.
        return true;
    }

    private readonly struct LobbyPlayerSnapshot
    {
        internal string PlayerName { get; }
        internal bool IsReady { get; }

        internal LobbyPlayerSnapshot(string playerName, bool isReady)
        {
            PlayerName = playerName;
            IsReady = isReady;
        }
    }
}
