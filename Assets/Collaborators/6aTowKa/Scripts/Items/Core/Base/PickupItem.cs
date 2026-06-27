using Unity.Netcode;
using UnityEngine;

public abstract class PickupItem : DraggableObject
{
    const int VIEWMODEL_LAYER_INDEX = 11;
    const int VIEWMODEL_RENDERING_LAYER_INDEX = 8;

    private bool isPickedUpServer = false;

    [SerializeField] private GameObject model;

    private Transform ownerTransform;
    private GameObject viewModel;
    private Vector3 hiddenPosition = new Vector3(0, -1000, 0);
    private PickUpContext context;

    private void OnValidate()
    {
        if (data != null)
        {
            if (data is not PickupItemData)
                Debug.LogError($"Data for {name} must be of type PickupItemData!", this);
        }
    }

    //OnInteract here is dragging.

    public void OnPickup(PickUpContext context) //Entry point for all items for button F!!!
    {
        PickUp(context);
    }

    public void OnDrop() //Entry point for all items for button Q!!!
    {
        Drop();
    }

    private void PickUp(PickUpContext context)
    {
        if (!NetworkManager.Singleton) return;
        this.context = context;
        RequestPickUpItemServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    protected void Drop()
    {
        if (ownerTransform == null) return;

        GetComponentInChildren<MeshRenderer>().enabled = true;
        rb.isKinematic = false;
        rb.rotation = Quaternion.identity;
        rb.position = ownerTransform.position;

        Destroy(viewModel);

        DropServerRpc();
        ownerTransform = null;

        playerInteraction.SetIsCarrying(false);
    }

    // Server RPCs

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void DropServerRpc()
    {
        isPickedUpServer = false;
        DropClientRpc();
    }


    [Rpc(SendTo.Server)]
    private void RequestPickUpItemServerRpc(ulong ownerId)
    {
        if (isPickedUpServer) return;
        isPickedUpServer = true;
        PickUpServer(ownerId);
    }

    // Client RPCs

    [Rpc(SendTo.ClientsAndHost)]
    private void PickUpClientRpc()
    {
        GetComponentInChildren<MeshRenderer>().enabled = false;
        rb.isKinematic = true;
        rb.position = hiddenPosition;

        if (IsOwner)
        {
            playerInteraction = context.PlayerInteraction;
            ownerTransform = context.OwnerTransform;
            MakeViewModel(context.ViewModelContainer);
        }

        context = null;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void DropClientRpc()
    {
        if (IsOwner) return;

        GetComponentInChildren<MeshRenderer>().enabled = true;
        rb.isKinematic = false;
    }

    // Server
    private void PickUpServer(ulong ownerId)
    {
        if (OwnerClientId != ownerId)
            GetComponent<NetworkObject>().ChangeOwnership(ownerId);

        PickUpClientRpc();
    }

    // Client
    private void MakeViewModel(Transform viewModelContainer)
    {
        viewModel = Instantiate(model, viewModelContainer);
        if (viewModel.transform.childCount > 0)
        {
            foreach (Transform child in viewModel.transform)
            {
                child.GetComponent<MeshRenderer>().renderingLayerMask = VIEWMODEL_RENDERING_LAYER_INDEX;
                child.GetComponent<MeshRenderer>().enabled = true;
                child.gameObject.layer = VIEWMODEL_LAYER_INDEX;
            }
        }
        else
        {
            viewModel.GetComponent<MeshRenderer>().renderingLayerMask = VIEWMODEL_RENDERING_LAYER_INDEX;
            viewModel.GetComponent<MeshRenderer>().enabled = true;
        }

        viewModel.layer = VIEWMODEL_LAYER_INDEX;

        ((PickupItemData)data).ViewmodelData.ApplyTo(viewModel.transform);
    }

    //other
    public int GetItemID()
    {
        return ((PickupItemData)data).ItemID;
    }
}
