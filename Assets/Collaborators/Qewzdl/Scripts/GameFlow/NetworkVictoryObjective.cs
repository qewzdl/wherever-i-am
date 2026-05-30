using System;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class NetworkVictoryObjective : NetworkBehaviour
{
    [SerializeField] private string objectiveId = "Objective";
    [SerializeField] private bool completedOnServerSpawn;

    public NetworkVariable<bool> Completed = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public string ObjectiveId => objectiveId;
    public bool IsCompleted => Completed.Value;

    public event Action<NetworkVictoryObjective, bool> LocalCompletionChanged;

    public override void OnNetworkSpawn()
    {
        if (IsServer && completedOnServerSpawn && !Completed.Value)
            Completed.Value = true;

        Completed.OnValueChanged += HandleCompletedChanged;
        LocalCompletionChanged?.Invoke(this, Completed.Value);
    }

    public override void OnNetworkDespawn()
    {
        Completed.OnValueChanged -= HandleCompletedChanged;
    }

    public bool TryCompleteServer()
    {
        return TrySetCompletedServer(true);
    }

    public bool TryResetServer()
    {
        return TrySetCompletedServer(false);
    }

    public bool TrySetCompletedServer(bool value)
    {
        if (!IsServer)
        {
            Debug.LogError($"{nameof(NetworkVictoryObjective)} can be changed only on server.", this);
            return false;
        }

        if (string.IsNullOrWhiteSpace(objectiveId))
        {
            Debug.LogError($"{nameof(NetworkVictoryObjective)} requires non-empty objective id.", this);
            return false;
        }

        if (Completed.Value == value)
            return false;

        Completed.Value = value;
        return true;
    }

    private void HandleCompletedChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue)
            return;

        LocalCompletionChanged?.Invoke(this, newValue);
    }
}