using System;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyAttackController))]
public sealed class EnemyAttackNetworkPresenter : NetworkBehaviour
{
    [SerializeField] private EnemyAttackController attackController;

    public event Action<EnemyAttackPhaseEvent> PhaseReceived;
    public event Action<EnemyAttackResult> ResultReceived;

    private void Awake()
    {
        if (attackController == null)
        {
            attackController = GetComponent<EnemyAttackController>();
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer || attackController == null)
        {
            return;
        }

        attackController.PhaseChanged += HandleServerPhaseChanged;
        attackController.AttackResolved += HandleServerAttackResolved;
    }

    public override void OnNetworkDespawn()
    {
        if (attackController == null)
        {
            return;
        }

        attackController.PhaseChanged -= HandleServerPhaseChanged;
        attackController.AttackResolved -= HandleServerAttackResolved;
    }

    private void HandleServerPhaseChanged(EnemyAttackPhaseEvent phaseEvent)
    {
        ReceivePhaseClientRpc(
            phaseEvent.Phase,
            phaseEvent.TargetIdentity,
            phaseEvent.AttackerPosition,
            phaseEvent.TargetPosition,
            phaseEvent.Reason
        );
    }

    private void HandleServerAttackResolved(EnemyAttackResult result)
    {
        ReceiveResultClientRpc(
            result.Type,
            result.TargetIdentity,
            result.AttackerPosition,
            result.TargetPosition
        );
    }

    [ClientRpc]
    private void ReceivePhaseClientRpc(
        EnemyAttackPhase phase,
        EnemyTargetIdentity targetIdentity,
        Vector3 attackerPosition,
        Vector3 targetPosition,
        EnemyAttackResultType reason
    )
    {
        PhaseReceived?.Invoke(
            new EnemyAttackPhaseEvent(
                phase,
                targetIdentity,
                attackerPosition,
                targetPosition,
                reason
            )
        );
    }

    [ClientRpc]
    private void ReceiveResultClientRpc(
        EnemyAttackResultType type,
        EnemyTargetIdentity targetIdentity,
        Vector3 attackerPosition,
        Vector3 targetPosition
    )
    {
        ResultReceived?.Invoke(
            EnemyAttackResult.Create(
                type,
                targetIdentity,
                attackerPosition,
                targetPosition
            )
        );
    }
}