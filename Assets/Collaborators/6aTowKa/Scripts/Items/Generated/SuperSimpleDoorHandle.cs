using Unity.Netcode;
using UnityEngine;

public class SuperSimpleDoorHandle : PassiveItem
{
    public override void Activate()
    {
        Drop();
        DestroyHandleRpc();
    }

    [Rpc(SendTo.Server)]
    private void DestroyHandleRpc()
    {
        NetworkObject netObj = GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Despawn(false);
            Destroy(netObj.gameObject);
        }
        else 
            Debug.LogWarning("Failed to find NetworkObject in parent hierarchy for SuperSimpleDoorHandle!", this);
    }
}
