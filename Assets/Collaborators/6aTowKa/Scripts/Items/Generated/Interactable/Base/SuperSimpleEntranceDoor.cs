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

    // Only for somebody standing at it: open to anybody anywhere, it let a
    // client skip the whole match with one message.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void DestroyDoorRpc(RpcParams rpcParams = default)
    {
        if (!PlayerRequestGuard.IsInPlayAndNear(NetworkManager, rpcParams.Receive.SenderClientId, this))
            return;

        NetworkObject netObj = GetComponentInParent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Despawn(false);
        }
        else
            Debug.LogWarning("Failed to find NetworkObject in parent hierarchy for SuperSimpleDoorHandle!", this);
    }
}
