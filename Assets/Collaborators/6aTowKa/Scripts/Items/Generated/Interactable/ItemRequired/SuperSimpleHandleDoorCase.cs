using Unity.Netcode;
using UnityEngine;

public class SuperSimpleHandleDoorCase : ItemRequiredInteractable
{
    // The case goes first: it is what checks the handle and uses it up, and
    // the handle may only be destroyed once it has been.
    protected override void InteractWith(IActivator item)
    {
        DestroyCaseRpc();
        item.Activate();
    }

    protected override void UnsuccessfulInteract()
    {
        Debug.Log("You need the Door Handle!", this);
    }

    public override void OnNetworkDespawn()
    {
        Destroy(gameObject);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void DestroyCaseRpc(RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        // The handle is checked on the client that interacts; here it has to
        // be held, and by somebody standing at the case.
        PickupItem handle = null;

        if (!PlayerRequestGuard.IsInPlayAndNear(NetworkManager, senderClientId, this) ||
            !PickupItem.TryFindHeldServer(NetworkManager, senderClientId, RequiredItemID, out handle))
        {
            return;
        }

        handle.MarkConsumedServer();

        NetworkObject netObj = GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Despawn(false);
        }
        else
            Debug.LogWarning("Failed to find NetworkObject in parent hierarchy for SuperSimpleDoorHandle!", this);
    }
}
