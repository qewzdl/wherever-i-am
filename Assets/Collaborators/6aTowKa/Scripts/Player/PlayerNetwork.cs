using Unity.Netcode;
using UnityEngine;

public class PlayerNetwork : PlayerNetworkComponent, IPlayerSignalListener
{
    public NetworkVariable<bool> PlayerIsCrouching = new NetworkVariable<bool>();

    [SerializeField] private PlayerAnimation playerAnimation;

    private bool listensToLocalCrouch;
    private bool listensToNetworkCrouch;

    protected override void OnPostInit(PlayerOrchestrator orch)
    {
        if (playerAnimation == null)
            playerAnimation = GetComponent<PlayerAnimation>();

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

    [Rpc(SendTo.Server)]
    private void SetNetworkPlayerIsCrouchingRpc(bool value)
    {
        if (!value && !CanStandUp())
        {
            CorrectOwnerCrouchStateRpc(PlayerIsCrouching.Value);
            return;
        }

        PlayerIsCrouching.Value = value;
    }

    [Rpc(SendTo.Owner)]
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
        return playerAnimation != null && playerAnimation.HasStandingClearance();
    }
}
