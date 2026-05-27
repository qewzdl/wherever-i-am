using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

public abstract class Item : DraggingObject
{
    [Header("View Model Data")]
    [SerializeField] ViewmodelItemData itemViewModelData;
    [SerializeField] string itemViewModelDataName;
    [Space]

    [SerializeField] private GameObject model;
    [SerializeField] private RenderingLayerMask renderingLayerMask;
    [SerializeField] private LayerMask layerMask;

    private Transform ownerTransform;
    private GameObject viewModel;
    private bool isPickedUp = false;
    private Vector3 hiddenPosition = new Vector3(0, -1000, 0);

    protected abstract void Action();

    public void PickUp(PickUpContext context)
    {
        if (!NetworkManager.Singleton) return;
        if (isPickedUp) return;

        ownerTransform = context.OwnerTransform;
        isPickedUp = true;

        MakeViewModel(context.ViewModelContainer);
        PickUpServerRpc(NetworkManager.Singleton.LocalClientId);
    }


    public void Drop()
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
        viewModel.GetComponent<MeshRenderer>().renderingLayerMask = renderingLayerMask;
        viewModel.GetComponent<MeshRenderer>().enabled = true;
        viewModel.layer = (int)Mathf.Log(layerMask.value, 2);

        var entry = itemViewModelData.GetEntry(itemViewModelDataName);
        entry.ApplyTo(viewModel.transform);
    }
}
