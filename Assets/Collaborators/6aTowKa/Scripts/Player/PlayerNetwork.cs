using Unity.Netcode;

public class PlayerNetwork : PlayerNetworkComponent, IPlayerSignalListener
{
    public NetworkVariable<bool> PlayerIsCrouching = new NetworkVariable<bool>();

    private bool listensToLocalCrouch;
    private bool listensToNetworkCrouch;

    protected override void OnPostInit(PlayerOrchestrator orch)
    {
        if (IsOwner)
        {
            signals.CrouchUpdateSignal.Listen(SetNetworkPlayerIsCrouchingRpc);
            listensToLocalCrouch = true;
            return;
        }

        PlayerIsCrouching.OnValueChanged += TriggerCrouchSyncSignal;
        listensToNetworkCrouch = true;

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
        PlayerIsCrouching.Value = value;
    }

    private void TriggerCrouchSyncSignal(bool oldValue, bool newValue)
    {
        if (oldValue != newValue)
            signals.CrouchSyncSignal.Trigger(newValue);
    }
}