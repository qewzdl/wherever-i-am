using Unity.Netcode;

public class PlayerNetwork : NetworkBehaviour
{
    public NetworkVariable<bool> PlayerIsCrouching = new NetworkVariable<bool>();

    [Rpc(SendTo.Server)]
    public void SetNetworkPlayerIsCrouchingRpc(bool value)
    {
        PlayerIsCrouching.Value = value;
    }

    public void Start()
    {
        
    }
}
