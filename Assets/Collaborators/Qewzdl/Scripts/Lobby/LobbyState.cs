using System;
using Unity.Netcode;
using UnityEngine;

public class LobbyState : NetworkBehaviour
{
    public const ulong NoRoomOwner = ulong.MaxValue;

    public NetworkList<LobbyPlayerData> Players { get; private set; }

    public NetworkVariable<LobbyPhase> Phase { get; } = new NetworkVariable<LobbyPhase>(
        LobbyPhase.Open,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<ulong> RoomOwnerClientId { get; } = new NetworkVariable<ulong>(
        NoRoomOwner,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> CanStartGame { get; } = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<LobbySettingsData> Settings { get; } = new NetworkVariable<LobbySettingsData>(
        LobbySettingsData.CreateDefault(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public event Action LobbyChanged;
    public event Action PlayersChanged;
    public event Action OwnerChanged;
    public event Action StartAvailabilityChanged;
    public event Action SettingsChanged;
    public event Action PhaseChanged;

    private void Awake()
    {
        Players = new NetworkList<LobbyPlayerData>();
    }

    public override void OnNetworkSpawn()
    {
        Players.OnListChanged += HandlePlayersChanged;
        RoomOwnerClientId.OnValueChanged += HandleRoomOwnerChanged;
        CanStartGame.OnValueChanged += HandleCanStartGameChanged;
        Settings.OnValueChanged += HandleSettingsChanged;
        Phase.OnValueChanged += HandlePhaseChanged;

        RaiseLobbyChanged();
    }

    public override void OnNetworkDespawn()
    {
        if (Players != null)
            Players.OnListChanged -= HandlePlayersChanged;

        RoomOwnerClientId.OnValueChanged -= HandleRoomOwnerChanged;
        CanStartGame.OnValueChanged -= HandleCanStartGameChanged;
        Settings.OnValueChanged -= HandleSettingsChanged;
        Phase.OnValueChanged -= HandlePhaseChanged;
    }

    public override void OnDestroy()
    {
        Players?.Dispose();
        base.OnDestroy();
    }

    private void HandlePlayersChanged(NetworkListEvent<LobbyPlayerData> changeEvent)
    {
        PlayersChanged?.Invoke();
        RaiseLobbyChanged();
    }

    private void HandleRoomOwnerChanged(ulong previousOwnerId, ulong newOwnerId)
    {
        OwnerChanged?.Invoke();
        RaiseLobbyChanged();
    }

    private void HandleCanStartGameChanged(bool previousValue, bool newValue)
    {
        StartAvailabilityChanged?.Invoke();
        RaiseLobbyChanged();
    }

    private void HandleSettingsChanged(LobbySettingsData previousValue, LobbySettingsData newValue)
    {
        SettingsChanged?.Invoke();
        RaiseLobbyChanged();
    }

    private void HandlePhaseChanged(LobbyPhase previousPhase, LobbyPhase newPhase)
    {
        PhaseChanged?.Invoke();
        RaiseLobbyChanged();
    }

    private void RaiseLobbyChanged()
    {
        LobbyChanged?.Invoke();
    }

    public bool TryGetPlayerIndex(ulong clientId, out int index)
    {
        for (int i = 0; i < Players.Count; i++)
        {
            if (Players[i].ClientId == clientId)
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }
}