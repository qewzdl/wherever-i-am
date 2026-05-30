using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class NetworkGameOutcome : NetworkBehaviour
{
    public const ulong NoWinningClientId = ulong.MaxValue;

    [Header("Victory")]
    [SerializeField] private EscapeVictoryMode victoryMode = EscapeVictoryMode.AnyPlayerEscapes;
    [SerializeField] private NetworkVictoryObjective[] requiredObjectives;

    public NetworkVariable<ulong> WinningClientId = new NetworkVariable<ulong>(
        NoWinningClientId,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<GameOutcomeState> State = new NetworkVariable<GameOutcomeState>(
        GameOutcomeState.Running,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> EscapedPlayersCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> RequiredEscapedPlayersCount = new NetworkVariable<int>(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly HashSet<ulong> escapedClientIds = new();

    public GameOutcomeState CurrentState => State.Value;
    public ulong CurrentWinningClientId => WinningClientId.Value;

    public event Action<GameOutcomeState, ulong> LocalOutcomeChanged;

    public override void OnNetworkSpawn()
    {
        State.OnValueChanged += HandleStateChanged;

        if (IsServer)
        {
            escapedClientIds.Clear();
            EscapedPlayersCount.Value = 0;
            UpdateRequiredEscapedPlayersCountServer();

            if (NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnectedServer;
        }

        if (State.Value != GameOutcomeState.Running)
            LocalOutcomeChanged?.Invoke(State.Value, WinningClientId.Value);
    }

    public override void OnNetworkDespawn()
    {
        State.OnValueChanged -= HandleStateChanged;

        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnectedServer;

        escapedClientIds.Clear();
    }

    public bool TryRegisterPlayerEscapeServer(NetworkObject playerNetworkObject)
    {
        if (!IsServer)
        {
            Debug.LogError($"{nameof(NetworkGameOutcome)} accepts escape registration only on server.", this);
            return false;
        }

        if (State.Value != GameOutcomeState.Running)
            return false;

        if (playerNetworkObject == null)
            return false;

        if (!playerNetworkObject.IsPlayerObject)
            return false;

        ulong clientId = playerNetworkObject.OwnerClientId;

        if (!IsConnectedClientServer(clientId))
            return false;

        if (!AreRequiredObjectivesCompletedServer())
            return false;

        escapedClientIds.Add(clientId);
        EscapedPlayersCount.Value = escapedClientIds.Count;
        UpdateRequiredEscapedPlayersCountServer();

        if (victoryMode == EscapeVictoryMode.AnyPlayerEscapes)
            return TryDeclareVictoryServer(clientId);

        if (escapedClientIds.Count >= RequiredEscapedPlayersCount.Value)
            return TryDeclareVictoryServer(clientId);

        return false;
    }

    public bool TryDeclareVictoryServer(ulong winningClientId)
    {
        if (!IsServer)
        {
            Debug.LogError($"{nameof(NetworkGameOutcome)} can declare victory only on server.", this);
            return false;
        }

        if (State.Value != GameOutcomeState.Running)
            return false;

        if (!AreRequiredObjectivesCompletedServer())
            return false;

        WinningClientId.Value = winningClientId;
        State.Value = GameOutcomeState.Victory;

        Debug.Log($"{nameof(NetworkGameOutcome)} declared victory. Winning client id: {winningClientId}", this);
        return true;
    }

    public bool TryDeclareDefeatServer()
    {
        if (!IsServer)
        {
            Debug.LogError($"{nameof(NetworkGameOutcome)} can declare defeat only on server.", this);
            return false;
        }

        if (State.Value != GameOutcomeState.Running)
            return false;

        WinningClientId.Value = NoWinningClientId;
        State.Value = GameOutcomeState.Defeat;

        Debug.Log($"{nameof(NetworkGameOutcome)} declared defeat.", this);
        return true;
    }

    public bool TryResetOutcomeServer()
    {
        if (!IsServer)
        {
            Debug.LogError($"{nameof(NetworkGameOutcome)} can reset outcome only on server.", this);
            return false;
        }

        escapedClientIds.Clear();

        WinningClientId.Value = NoWinningClientId;
        EscapedPlayersCount.Value = 0;
        UpdateRequiredEscapedPlayersCountServer();
        State.Value = GameOutcomeState.Running;

        return true;
    }

    private bool AreRequiredObjectivesCompletedServer()
    {
        if (requiredObjectives == null || requiredObjectives.Length == 0)
            return true;

        for (int i = 0; i < requiredObjectives.Length; i++)
        {
            NetworkVictoryObjective objective = requiredObjectives[i];

            if (objective == null)
            {
                Debug.LogError($"{nameof(NetworkGameOutcome)} has missing required objective reference at index {i}.", this);
                return false;
            }

            if (!objective.IsCompleted)
                return false;
        }

        return true;
    }

    private void HandleClientDisconnectedServer(ulong clientId)
    {
        if (!IsServer)
            return;

        escapedClientIds.Remove(clientId);
        EscapedPlayersCount.Value = escapedClientIds.Count;
        UpdateRequiredEscapedPlayersCountServer();
    }

    private void UpdateRequiredEscapedPlayersCountServer()
    {
        if (!IsServer)
            return;

        if (NetworkManager == null)
        {
            Debug.LogError($"{nameof(NetworkGameOutcome)} requires active {nameof(NetworkManager)} on server.", this);
            return;
        }

        if (victoryMode == EscapeVictoryMode.AnyPlayerEscapes)
        {
            RequiredEscapedPlayersCount.Value = 1;
            return;
        }

        int connectedClientsCount = NetworkManager.ConnectedClientsIds.Count;
        RequiredEscapedPlayersCount.Value = Mathf.Max(1, connectedClientsCount);
    }

    private bool IsConnectedClientServer(ulong clientId)
    {
        if (NetworkManager == null)
            return false;

        IReadOnlyList<ulong> connectedClientIds = NetworkManager.ConnectedClientsIds;

        for (int i = 0; i < connectedClientIds.Count; i++)
        {
            if (connectedClientIds[i] == clientId)
                return true;
        }

        return false;
    }

    private void HandleStateChanged(GameOutcomeState oldState, GameOutcomeState newState)
    {
        if (oldState == newState)
            return;

        LocalOutcomeChanged?.Invoke(newState, WinningClientId.Value);
    }
}