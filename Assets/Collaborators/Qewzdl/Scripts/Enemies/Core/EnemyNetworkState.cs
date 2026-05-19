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

    private readonly NetworkVariable<EnemyAttackPhaseSnapshot> currentAttackPhase = new(
        EnemyAttackPhaseSnapshot.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public event Action<EnemyState, EnemyState> StateChanged;
    public event Action<EnemyTargetIdentity, EnemyTargetIdentity> TargetChanged;
    public event Action<EnemyAttackPhaseSnapshot, EnemyAttackPhaseSnapshot> AttackPhaseChanged;

    public EnemyState CurrentState => currentState.Value;
    public EnemyTargetIdentity CurrentTargetIdentity => currentTargetIdentity.Value;
    public EnemyAttackPhaseSnapshot CurrentAttackPhase => currentAttackPhase.Value;

    public ulong CurrentTargetClientId => currentTargetIdentity.Value.OwnerClientId;
    public bool HasTarget => currentTargetIdentity.Value.HasTarget;
    public bool HasActiveAttackPhase => currentAttackPhase.Value.IsActive;

    public override void OnNetworkSpawn()
    {
        currentState.OnValueChanged += HandleStateChanged;
        currentTargetIdentity.OnValueChanged += HandleTargetChanged;
        currentAttackPhase.OnValueChanged += HandleAttackPhaseChanged;
    }

    public override void OnNetworkDespawn()
    {
        currentState.OnValueChanged -= HandleStateChanged;
        currentTargetIdentity.OnValueChanged -= HandleTargetChanged;
        currentAttackPhase.OnValueChanged -= HandleAttackPhaseChanged;
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

    public void SetAttackPhaseServer(EnemyAttackPhaseEvent phaseEvent)
    {
        if (!IsServer)
        {
            return;
        }

        if (!TryGetServerTime(out double serverTime))
        {
            return;
        }

        SetAttackPhaseSnapshotServer(
            EnemyAttackPhaseSnapshot.FromEvent(phaseEvent, serverTime)
        );
    }

    public void ClearTargetServer()
    {
        SetTargetIdentityServer(EnemyTargetIdentity.None);
    }

    public void ClearAttackPhaseServer()
    {
        if (!IsServer)
        {
            return;
        }

        if (!TryGetServerTime(out double serverTime))
        {
            return;
        }

        SetAttackPhaseSnapshotServer(
            EnemyAttackPhaseSnapshot.CreateIdle(serverTime)
        );
    }

    private void SetAttackPhaseSnapshotServer(EnemyAttackPhaseSnapshot snapshot)
    {
        if (!IsServer || currentAttackPhase.Value == snapshot)
        {
            return;
        }

        currentAttackPhase.Value = snapshot;
    }

    private bool TryGetServerTime(out double serverTime)
    {
        serverTime = 0d;

        if (NetworkManager == null || !NetworkManager.IsListening)
        {
            Debug.LogError(
                $"{nameof(EnemyNetworkState)} cannot synchronize attack phase without an active {nameof(NetworkManager)}.",
                this
            );

            return false;
        }

        serverTime = NetworkManager.ServerTime.Time;
        return true;
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

    private void HandleAttackPhaseChanged(
        EnemyAttackPhaseSnapshot previousPhase,
        EnemyAttackPhaseSnapshot nextPhase
    )
    {
        AttackPhaseChanged?.Invoke(previousPhase, nextPhase);
    }
}