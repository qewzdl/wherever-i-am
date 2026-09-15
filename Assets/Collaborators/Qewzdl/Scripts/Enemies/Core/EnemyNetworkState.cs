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

    private readonly NetworkVariable<EnemyHeardNoiseSnapshot> heardNoise = new(
        EnemyHeardNoiseSnapshot.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private uint heardNoiseCount;

    public event Action<EnemyState, EnemyState> StateChanged;
    public event Action<EnemyTargetIdentity, EnemyTargetIdentity> TargetChanged;
    public event Action<EnemyPosture, EnemyPosture> PostureChanged;
    public event Action<EnemyAttackPhaseSnapshot, EnemyAttackPhaseSnapshot> AttackPhaseChanged;

    // The score, because that is all a listener needs. Whether it was loud
    // enough to be worth a sound is a presentation question and is answered
    // where the sound lives.
    public event Action<float, GameplayNoiseSourceType> HeardNoise;

    public EnemyState CurrentState => currentState.Value;
    public EnemyTargetIdentity CurrentTargetIdentity => currentTargetIdentity.Value;
    public EnemyPosture CurrentPosture => currentPosture.Value;
    public EnemyAttackPhaseSnapshot CurrentAttackPhase => currentAttackPhase.Value;

    public ulong CurrentTargetClientId => currentTargetIdentity.Value.OwnerClientId;
    public bool HasTarget => currentTargetIdentity.Value.HasTarget;
    public bool HasActiveAttackPhase => currentAttackPhase.Value.IsActive;

    public override void OnNetworkSpawn()
    {
        currentState.OnValueChanged += HandleStateChanged;
        currentTargetIdentity.OnValueChanged += HandleTargetChanged;
        currentPosture.OnValueChanged += HandlePostureChanged;
        currentAttackPhase.OnValueChanged += HandleAttackPhaseChanged;
        heardNoise.OnValueChanged += HandleHeardNoiseChanged;
    }

    public override void OnNetworkDespawn()
    {
        currentState.OnValueChanged -= HandleStateChanged;
        currentTargetIdentity.OnValueChanged -= HandleTargetChanged;
        currentPosture.OnValueChanged -= HandlePostureChanged;
        currentAttackPhase.OnValueChanged -= HandleAttackPhaseChanged;
        heardNoise.OnValueChanged -= HandleHeardNoiseChanged;
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

    }

    public void SetPostureServer(EnemyPosture nextPosture)
    {
        if (!IsServer || currentPosture.Value == nextPosture)
        {
            return;
        }

        currentPosture.Value = nextPosture;
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

    // Counts from one and never repeats, so a second noise of exactly the same
    // loudness still reads as a second noise. Late joiners get whatever the
    // last report was in their initial sync and no event with it, which is
    // right: they did not hear it happen.
    public void ReportHeardNoiseServer(
        float score,
        GameplayNoiseSourceType source)
    {
        if (!IsServer)
        {
            return;
        }

        heardNoiseCount++;
        heardNoise.Value = new EnemyHeardNoiseSnapshot(
            heardNoiseCount,
            score,
            source);
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

    private void HandlePostureChanged(
        EnemyPosture previousPosture,
        EnemyPosture nextPosture
    )
    {
        PostureChanged?.Invoke(previousPosture, nextPosture);
    }

    private void HandleHeardNoiseChanged(
        EnemyHeardNoiseSnapshot previousReport,
        EnemyHeardNoiseSnapshot nextReport
    )
    {
        if (!nextReport.HasReport)
        {
            return;
        }

        HeardNoise?.Invoke(nextReport.Score, nextReport.Source);
    }

    private void HandleAttackPhaseChanged(
        EnemyAttackPhaseSnapshot previousPhase,
        EnemyAttackPhaseSnapshot nextPhase
    )
    {
        AttackPhaseChanged?.Invoke(previousPhase, nextPhase);
    }
}
