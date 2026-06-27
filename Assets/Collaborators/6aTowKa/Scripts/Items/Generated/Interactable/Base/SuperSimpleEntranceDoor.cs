using Unity.Netcode;
using UnityEngine;

public class SuperSimpleEntranceDoor : InteractableObject
{
    public override void OnInteract(InteractionContext context)
    {
        DestroyDoorRpc();
    }

    public override void OnNetworkDespawn()
    {
        Destroy(transform.parent.gameObject);
    }

    [Rpc(SendTo.Server)]
    private void DestroyDoorRpc()
    {
        NetworkObject netObj = GetComponentInParent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Despawn(false);
        }
        else
            Debug.LogWarning("Failed to find NetworkObject in parent hierarchy for SuperSimpleDoorHandle!", this);
    }
}
