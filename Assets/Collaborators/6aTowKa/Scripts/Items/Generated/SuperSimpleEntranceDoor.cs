using Unity.Netcode;
using UnityEngine;

public class SuperSimpleEntranceDoor : InteractableObject
{
    public override void OnInteract(InteractionContext context)
    {
        DestroyDoorRpc();
    }

    [Rpc(SendTo.Server)]
    private void DestroyDoorRpc()
    {
        NetworkObject netObj = GetComponentInParent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Despawn(false);
            Destroy(netObj.gameObject);
        }
        else
            Debug.LogWarning("Failed to find NetworkObject for SuperSimpleHandleDoorCase!", this);
    }
}
