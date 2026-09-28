using Unity.Netcode;
using UnityEngine;

public class SuperSimpleDoorHandle : PassiveItem
{
    public override void Activate()
    {
        Drop();
        DestroyHandleRpc();
    }

    public override void OnNetworkDespawn()
    {
        Destroy(gameObject);
    }

    // Its carrier's to destroy, and only once a door has taken it. Open to
    // anybody, one message from any client destroyed the only handle in the
    // house and left a match nobody could finish.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void DestroyHandleRpc()
    {
        if (!IsConsumedServer)
            return;

        NetworkObject netObj = GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Despawn(false);
        }
        else 
            Debug.LogWarning("Failed to find NetworkObject in parent hierarchy for SuperSimpleDoorHandle!", this);
    }
}
