using Unity.Netcode;
using UnityEngine;

public class SuperSimpleHandleDoorCase : ItemRequiredInteractable
{
    protected override void InteractWith(IActivator item)
    {
        item.Activate();
        DestroyCaseRpc();
    }

    protected override void UnsuccessfulInteract()
    {
        Debug.Log("You need the Door Handle!", this);
    }

    public override void OnNetworkDespawn()
    {
        Destroy(gameObject);
    }

    [Rpc(SendTo.Server)]
    private void DestroyCaseRpc()
    {
        NetworkObject netObj = GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Despawn(false);
        }
        else
            Debug.LogWarning("Failed to find NetworkObject in parent hierarchy for SuperSimpleDoorHandle!", this);
    }
}
