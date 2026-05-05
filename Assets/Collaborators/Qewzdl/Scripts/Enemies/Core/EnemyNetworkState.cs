using System;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyNetworkState : NetworkBehaviour
{
    private readonly NetworkVariable<EnemyState> currentState = new(
        EnemyState.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<EnemyTargetIdentity> currentTargetIdentity = new(
        EnemyTargetIdentity.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public event Action<EnemyState, EnemyState> StateChanged;
    public event Action<EnemyTargetIdentity, EnemyTargetIdentity> TargetChanged;

    public EnemyState CurrentState => currentState.Value;
    public EnemyTargetIdentity CurrentTargetIdentity => currentTargetIdentity.Value;
    public ulong CurrentTargetClientId => currentTargetIdentity.Value.OwnerClientId;
    public bool HasTarget => currentTargetIdentity.Value.HasTarget;

    public override void OnNetworkSpawn()
    {
        currentState.OnValueChanged += HandleStateChanged;
        currentTargetIdentity.OnValueChanged += HandleTargetChanged;
    }

    public override void OnNetworkDespawn()
    {
        currentState.OnValueChanged -= HandleStateChanged;
        currentTargetIdentity.OnValueChanged -= HandleTargetChanged;
    }

    public bool TryGetCurrentTargetNetworkObject(out NetworkObject targetNetworkObject)
    {
        return currentTargetIdentity.Value.TryGetNetworkObject(out targetNetworkObject);
    }

    public void SetStateServer(EnemyState nextState)
    {
        if (!IsServer || currentState.Value == nextState)
        {
            return;
        }

        currentState.Value = nextState;
    }

    public void SetTargetIdentityServer(EnemyTargetIdentity targetIdentity)
    {
        if (!IsServer || currentTargetIdentity.Value == targetIdentity)
        {
            return;
        }

        currentTargetIdentity.Value = targetIdentity;
    }

    public void ClearTargetServer()
    {
        SetTargetIdentityServer(EnemyTargetIdentity.None);
    }

    private void HandleStateChanged(EnemyState previousState, EnemyState nextState)
    {
        StateChanged?.Invoke(previousState, nextState);
    }

    private void HandleTargetChanged(
        EnemyTargetIdentity previousTarget,
        EnemyTargetIdentity nextTarget
    )
    {
        TargetChanged?.Invoke(previousTarget, nextTarget);
    }
}