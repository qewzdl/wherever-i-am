using Unity.Netcode;
using UnityEngine;

public class PlayerNetwork :
    PlayerNetworkComponent,
    IPlayerSignalListener,
    IReplicatedPlayerStateService
{
    public NetworkVariable<bool> PlayerIsCrouching = new NetworkVariable<bool>();

    [SerializeField] private PlayerPostureController playerPosture;

    private bool listensToLocalCrouch;
    private bool listensToNetworkCrouch;

    public bool IsCrouching => PlayerIsCrouching.Value;

    protected override void OnPostInit(PlayerOrchestrator orch)
    {
        if (playerPosture == null)
            playerPosture = GetComponent<PlayerPostureController>();

        PlayerIsCrouching.OnValueChanged += TriggerCrouchSyncSignal;
        listensToNetworkCrouch = true;

        if (IsOwner)
        {
            signals.CrouchUpdateSignal.Listen(SetNetworkPlayerIsCrouchingRpc);
            listensToLocalCrouch = true;
            return;
        }

        signals.CrouchSyncSignal.Trigger(PlayerIsCrouching.Value);
    }

    public void Cleanup()
    {
        if (listensToLocalCrouch)
            signals.CrouchUpdateSignal.Unlisten(SetNetworkPlayerIsCrouchingRpc);

        if (listensToNetworkCrouch)
            PlayerIsCrouching.OnValueChanged -= TriggerCrouchSyncSignal;

        listensToLocalCrouch = false;
        listensToNetworkCrouch = false;
    }

    // An RPC is invoked on a NetworkObject, and this one is somebody's body.
    // Left at the default permission any client could send it to any other
    // player's object and stand a hiding player up - which is the whole game,
    // undone from the other side of the map. Every other player-owned request
    // in the project already says Owner; this one was the exception.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SetNetworkPlayerIsCrouchingRpc(bool value)
    {
        if (!value && !CanStandUp())
        {
            CorrectOwnerCrouchStateRpc(PlayerIsCrouching.Value);
            return;
        }

        PlayerIsCrouching.Value = value;
    }

    // The other half of the same hole: this is the server correcting an owner
    // who could not stand, so only the server has any business sending it.
    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void CorrectOwnerCrouchStateRpc(bool value)
    {
        signals.CrouchSyncSignal.Trigger(value);
    }

    private void TriggerCrouchSyncSignal(bool oldValue, bool newValue)
    {
        if (oldValue != newValue)
            signals.CrouchSyncSignal.Trigger(newValue);
    }

    private bool CanStandUp()
    {
        return playerPosture != null && playerPosture.HasStandingClearance();
    }
}
