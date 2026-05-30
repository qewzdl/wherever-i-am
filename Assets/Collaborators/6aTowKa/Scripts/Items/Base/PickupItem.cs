using Unity.Netcode;
using UnityEngine;

public abstract class PickupItem : DraggableObject
{
    const int VIEWMODEL_LAYER_INDEX = 11;
    const int VIEWMODEL_RENDERING_LAYER_INDEX = 8;
    

    [SerializeField] private GameObject model;

    private PickupItemData itemData;
    private Transform ownerTransform;
    private GameObject viewModel;
    private bool isPickedUp = false;
    private Vector3 hiddenPosition = new Vector3(0, -1000, 0);

    protected override void OnValidate()
    {
        base.OnValidate();  

        if (data != null)
        {
            if (data is PickupItemData targetData)
                itemData = targetData;
            else
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
        if (isPickedUp) return;
        playerInteraction = context.PlayerInteraction;

        ownerTransform = context.OwnerTransform;
        isPickedUp = true;

        MakeViewModel(context.ViewModelContainer);
        PickUpServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    protected void Drop()
    {
        if (!isPickedUp) return;

        if (ownerTransform == null) return;

        GetComponentInChildren<MeshRenderer>().enabled = true;
        rb.isKinematic = false;
        rb.rotation = Quaternion.identity;
        rb.position = ownerTransform.position;

        Destroy(viewModel);

        isPickedUp = false;

        DropServerRpc(ownerTransform.position);
        ownerTransform = null;

        playerInteraction.SetIsCarrying(false);
    }

    // Server RPCs

    [Rpc(SendTo.Server)]
    private void PickUpServerRpc(ulong ownerId)
    {
        if (OwnerClientId != ownerId)
            GetComponent<NetworkObject>().ChangeOwnership(ownerId);

        PickUpClientRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void DropServerRpc(Vector3 dropPosition)
    {
        DropClientRpc(dropPosition);
    }

    // Client RPCs

    [Rpc(SendTo.ClientsAndHost)]
    private void PickUpClientRpc()
    {
        GetComponentInChildren<MeshRenderer>().enabled = false;
        rb.isKinematic = true;
        rb.position = hiddenPosition;

        if (!IsOwner)
        {
            isPickedUp = true;
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void DropClientRpc(Vector3 dropPosition)
    {
        if (!isPickedUp || IsOwner) return;

        GetComponentInChildren<MeshRenderer>().enabled = true;
        rb.isKinematic = false;

        ownerTransform = null;
        isPickedUp = false;
    }

    // Client
    private void MakeViewModel(Transform viewModelContainer)
    {
        viewModel = Instantiate(model, viewModelContainer);
        viewModel.GetComponent<MeshRenderer>().renderingLayerMask = VIEWMODEL_RENDERING_LAYER_INDEX;
        viewModel.GetComponent<MeshRenderer>().enabled = true;
        viewModel.layer = VIEWMODEL_LAYER_INDEX;

        var entry = ViewModelsItemsData.Instance.GetEntry(itemData.ItemViewModelDataName);
        entry.ApplyTo(viewModel.transform);
    }

    //other
    public int GetItemID()
    {
        return itemData.ItemID;
    }
}
