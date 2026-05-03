using Unity.Netcode;

public class PlayerNetwork : PlayerNetworkComponent, IPlayerSignalListener
{
    public NetworkVariable<bool> PlayerIsCrouching = new NetworkVariable<bool>();

    protected override void OnPostInit(PlayerOrchestrator orch)
    {
        signals.CrouchUpdateSignal.Listen(SetNetworkPlayerIsCrouchingRpc);
        if (!IsOwner) 
            PlayerIsCrouching.OnValueChanged += TriggerCrouchSyncSignal;
    }

    public void Cleanup()
    {
        signals.CrouchUpdateSignal.Unlisten(SetNetworkPlayerIsCrouchingRpc);
        PlayerIsCrouching.OnValueChanged -= TriggerCrouchSyncSignal; // ???????????
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
