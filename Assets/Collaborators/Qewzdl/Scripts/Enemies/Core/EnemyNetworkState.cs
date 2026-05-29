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

    private readonly NetworkVariable<EnemyPosture> currentPosture = new(
        EnemyPosture.Standing,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<EnemyAttackPhaseSnapshot> currentAttackPhase = new(
        EnemyAttackPhaseSnapshot.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<EnemyThreatMusicState> currentThreatMusicState = new(
        EnemyThreatMusicState.Calm,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private bool hasCombatThreatSinceCalm;

    public event Action<EnemyState, EnemyState> StateChanged;
    public event Action<EnemyTargetIdentity, EnemyTargetIdentity> TargetChanged;
    public event Action<EnemyPosture, EnemyPosture> PostureChanged;
    public event Action<EnemyAttackPhaseSnapshot, EnemyAttackPhaseSnapshot> AttackPhaseChanged;
    public event Action<EnemyThreatMusicState, EnemyThreatMusicState> ThreatMusicStateChanged;

    public EnemyState CurrentState => currentState.Value;
    public EnemyTargetIdentity CurrentTargetIdentity => currentTargetIdentity.Value;
    public EnemyPosture CurrentPosture => currentPosture.Value;
    public EnemyAttackPhaseSnapshot CurrentAttackPhase => currentAttackPhase.Value;
    public EnemyThreatMusicState CurrentThreatMusicState => currentThreatMusicState.Value;

    public ulong CurrentTargetClientId => currentTargetIdentity.Value.OwnerClientId;
    public bool HasTarget => currentTargetIdentity.Value.HasTarget;
    public bool HasActiveAttackPhase => currentAttackPhase.Value.IsActive;

    public override void OnNetworkSpawn()
    {
        currentState.OnValueChanged += HandleStateChanged;
        currentTargetIdentity.OnValueChanged += HandleTargetChanged;
        currentPosture.OnValueChanged += HandlePostureChanged;
        currentAttackPhase.OnValueChanged += HandleAttackPhaseChanged;
        currentThreatMusicState.OnValueChanged += HandleThreatMusicStateChanged;
    }

    public override void OnNetworkDespawn()
    {
        currentState.OnValueChanged -= HandleStateChanged;
        currentTargetIdentity.OnValueChanged -= HandleTargetChanged;
        currentPosture.OnValueChanged -= HandlePostureChanged;
        currentAttackPhase.OnValueChanged -= HandleAttackPhaseChanged;
        currentThreatMusicState.OnValueChanged -= HandleThreatMusicStateChanged;
    }

    public bool TryGetCurrentTargetNetworkObject(out NetworkObject targetNetworkObject)
    {
        return currentTargetIdentity.Value.TryGetNetworkObject(out targetNetworkObject);
    }

    public void SetStateServer(EnemyState nextState)
    {
        if (!IsServer)
        {
            return;
        }

        if (currentState.Value != nextState)
        {
            currentState.Value = nextState;
        }

        RefreshThreatMusicStateServer();
    }

    public void SetTargetIdentityServer(EnemyTargetIdentity targetIdentity)
    {
        if (!IsServer)
        {
            return;
        }

        if (currentTargetIdentity.Value != targetIdentity)
        {
            currentTargetIdentity.Value = targetIdentity;
        }

        RefreshThreatMusicStateServer();
    }

    public void SetPostureServer(EnemyPosture nextPosture)
    {
        if (!IsServer || currentPosture.Value == nextPosture)
        {
            return;
        }

        currentPosture.Value = nextPosture;
    }

    public void SetThreatMusicStateServer(EnemyThreatMusicState nextThreatMusicState)
    {
        if (!IsServer)
        {
            return;
        }

        ApplyThreatMusicStateServer(nextThreatMusicState);
    }

    public void SetDeadThreatMusicStateServer()
    {
        if (!IsServer)
        {
            return;
        }

        ApplyThreatMusicStateServer(EnemyThreatMusicState.Dead);
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

    private void RefreshThreatMusicStateServer()
    {
        if (!IsServer)
        {
            return;
        }

        EnemyThreatMusicState nextThreatMusicState = ResolveThreatMusicState(currentState.Value);
        ApplyThreatMusicStateServer(nextThreatMusicState);
    }

    private EnemyThreatMusicState ResolveThreatMusicState(EnemyState enemyState)
    {
        switch (enemyState)
        {
            case EnemyState.Idle:
            case EnemyState.Patrol:
                return EnemyThreatMusicState.Calm;

            case EnemyState.Chase:
            case EnemyState.Attack:
                return EnemyThreatMusicState.Combat;

            case EnemyState.Investigate:
                return hasCombatThreatSinceCalm
                    ? EnemyThreatMusicState.LostTarget
                    : EnemyThreatMusicState.Suspicious;

            default:
                Debug.LogError(
                    $"{nameof(EnemyNetworkState)} cannot resolve threat music state for unsupported enemy state {enemyState}.",
                    this
                );

                return EnemyThreatMusicState.Calm;
        }
    }

    private void ApplyThreatMusicStateServer(EnemyThreatMusicState nextThreatMusicState)
    {
        if (!IsServer)
        {
            return;
        }

        switch (nextThreatMusicState)
        {
            case EnemyThreatMusicState.Calm:
            case EnemyThreatMusicState.Dead:
                hasCombatThreatSinceCalm = false;
                break;

            case EnemyThreatMusicState.Combat:
                hasCombatThreatSinceCalm = true;
                break;

            case EnemyThreatMusicState.Suspicious:
            case EnemyThreatMusicState.LostTarget:
                break;

            default:
                Debug.LogError(
                    $"{nameof(EnemyNetworkState)} received unsupported threat music state {nextThreatMusicState}.",
                    this
                );

                return;
        }

        if (currentThreatMusicState.Value == nextThreatMusicState)
        {
            return;
        }

        currentThreatMusicState.Value = nextThreatMusicState;
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

    private void HandlePostureChanged(
        EnemyPosture previousPosture,
        EnemyPosture nextPosture
    )
    {
        PostureChanged?.Invoke(previousPosture, nextPosture);
    }

    private void HandleAttackPhaseChanged(
        EnemyAttackPhaseSnapshot previousPhase,
        EnemyAttackPhaseSnapshot nextPhase
    )
    {
        AttackPhaseChanged?.Invoke(previousPhase, nextPhase);
    }

    private void HandleThreatMusicStateChanged(
        EnemyThreatMusicState previousState,
        EnemyThreatMusicState nextState
    )
    {
        ThreatMusicStateChanged?.Invoke(previousState, nextState);
    }
}