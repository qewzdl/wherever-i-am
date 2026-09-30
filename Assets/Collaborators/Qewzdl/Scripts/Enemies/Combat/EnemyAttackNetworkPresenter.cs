using System;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(EnemyAttackController))]
public sealed class EnemyAttackNetworkPresenter : NetworkBehaviour
{
    [SerializeField] private EnemyAttackController attackController;

    private NetworkVariable<EnemyAttackPresentationFrame> currentPhaseFrame =
        new NetworkVariable<EnemyAttackPresentationFrame>(
            EnemyAttackPresentationFrame.Empty,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private bool isConfigured;
    private uint phaseSequenceId;
    private uint resultSequenceId;
    private uint lastDeliveredPhaseSequenceId;
    private uint lastDeliveredResultSequenceId;

    public event Action<EnemyAttackPhaseEvent> PhaseReceived;
    public event Action<EnemyAttackResult> ResultReceived;

    private void Awake()
    {
        isConfigured = ValidateReferences();
    }

    public override void OnNetworkSpawn()
    {
        if (!isConfigured)
        {
            return;
        }

        if (IsClient)
        {
            currentPhaseFrame.OnValueChanged += HandlePhaseFrameChanged;
            TryDeliverInitialPhaseFrame();
        }

        if (IsServer)
        {
            attackController.PhaseChanged += HandleServerPhaseChanged;
            attackController.AttackResolved += HandleServerAttackResolved;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsClient)
        {
            currentPhaseFrame.OnValueChanged -= HandlePhaseFrameChanged;
        }

        if (isConfigured && IsServer)
        {
            attackController.PhaseChanged -= HandleServerPhaseChanged;
            attackController.AttackResolved -= HandleServerAttackResolved;
        }

        lastDeliveredPhaseSequenceId = 0;
        lastDeliveredResultSequenceId = 0;
    }

    private bool ValidateReferences()
    {
        if (attackController != null)
        {
            return true;
        }

        Debug.LogError(
            $"{nameof(EnemyAttackNetworkPresenter)} requires explicit {nameof(EnemyAttackController)} reference.",
            this
        );

        enabled = false;
        return false;
    }

    // Every phase is sent as it happens, in the same stream as the results.
    //
    // The variable alone carries only where the attack is at the end of a
    // network tick: two phases inside one tick reached a guest as the second,
    // and a result - sent at once - arrived before the phase the server had
    // entered in the same breath. The host, whose copy of the variable is the
    // one being written, saw every phase in order. The variable stays for a
    // copy that spawns mid-attack and needs to know where it is.
    private void HandleServerPhaseChanged(EnemyAttackPhaseEvent phaseEvent)
    {
        EnemyAttackPresentationFrame frame = EnemyAttackPresentationFrame.FromPhaseEvent(
            phaseEvent,
            NextPhaseSequenceId()
        );

        currentPhaseFrame.Value = frame;
        ReceivePhaseClientRpc(frame);
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
    private void ReceivePhaseClientRpc(EnemyAttackPresentationFrame frame)
    {
        DeliverPhaseFrame(frame);
    }

    private void HandleServerAttackResolved(EnemyAttackResult result)
    {
        EnemyAttackResolutionFrame frame = EnemyAttackResolutionFrame.FromResult(
            result,
            NextResultSequenceId()
        );

        ReceiveResultClientRpc(frame);
    }

    private void HandlePhaseFrameChanged(
        EnemyAttackPresentationFrame previousFrame,
        EnemyAttackPresentationFrame currentFrame
    )
    {
        DeliverPhaseFrame(currentFrame);
    }

    private void TryDeliverInitialPhaseFrame()
    {
        DeliverPhaseFrame(currentPhaseFrame.Value);
    }

    private void DeliverPhaseFrame(EnemyAttackPresentationFrame frame)
    {
        // Each phase arrives twice - once as it happens, once as the
        // variable catches up - and only the first, or anything older, is
        // turned away.
        if (!frame.HasValue || frame.SequenceId <= lastDeliveredPhaseSequenceId)
        {
            return;
        }

        lastDeliveredPhaseSequenceId = frame.SequenceId;
        PhaseReceived?.Invoke(frame.ToPhaseEvent());
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
    private void ReceiveResultClientRpc(EnemyAttackResolutionFrame frame)
    {
        if (!frame.HasValue || frame.SequenceId == lastDeliveredResultSequenceId)
        {
            return;
        }

        lastDeliveredResultSequenceId = frame.SequenceId;
        ResultReceived?.Invoke(frame.ToResult());
    }

    private uint NextPhaseSequenceId()
    {
        phaseSequenceId++;

        if (phaseSequenceId == 0)
        {
            phaseSequenceId = 1;
        }

        return phaseSequenceId;
    }

    private uint NextResultSequenceId()
    {
        resultSequenceId++;

        if (resultSequenceId == 0)
        {
            resultSequenceId = 1;
        }

        return resultSequenceId;
    }
}