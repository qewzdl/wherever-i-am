using Unity.Netcode;
using UnityEngine;

public abstract class PickupItem : DraggableObject
{
    const int VIEWMODEL_LAYER_INDEX = 11;
    const uint VIEWMODEL_RENDERING_LAYER_INDEX = (1u << 8);

    private NetworkVariable<bool> netIsPickedUp = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [SerializeField] private GameObject model;

    private Transform ownerTransform;
    private GameObject viewModel;
    private Vector3 hiddenPosition = new Vector3(0, -1000, 0);
    private PickUpContext context;
    private MeshRenderer meshRenderer;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;

    protected override void Awake()
    {
        base.Awake();

        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
    }

    private void OnValidate()
    {
        if (data != null)
        {
            if (data is not PickupItemData)
                Debug.LogError($"Data for {name} must be of type PickupItemData!", this);
        }
    }

    protected override void OnOwnershipChanged(ulong previous, ulong current)
    {
        base.OnOwnershipChanged(previous, current);

        if (!IsServer ||
            current != NetworkManager.ServerClientId ||
            !netIsPickedUp.Value)
        {
            return;
        }

        netIsPickedUp.Value = false;

        rb.position = spawnPosition;
        rb.rotation = spawnRotation;

        DropClientRpc();
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

    protected override bool CanStartDragging()
    {
        return !netIsPickedUp.Value;
    }

    private void PickUp(PickUpContext context)
    {
        if (NetworkManager == null) return;
        this.context = context;
        RequestPickUpItemServerRpc(NetworkManager.LocalClientId);
    }

    protected void Drop()
    {
        if (ownerTransform == null) return;

        var renderer = GetMeshRenderer();
        if (renderer != null)
            renderer.enabled = true;

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
        netIsPickedUp.Value = false;
        DropClientRpc();
    }


    [Rpc(SendTo.Server)]
    private void RequestPickUpItemServerRpc(ulong ownerId, RpcParams rpcParams = default)
    {
        if (netIsPickedUp.Value || netIsDragging.Value)
        {
            DenyPickupRpc(RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
            return;
        }
        netIsPickedUp.Value = true;
        PickUpServer(ownerId);
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void DenyPickupRpc(RpcParams rpcParams = default)
    {
        context.PlayerInteraction.DenyPickup();
        context = null;
    }

    // Client RPCs

    [Rpc(SendTo.ClientsAndHost)]
    private void HidePickupClientRpc()
    {
        var renderer = GetMeshRenderer();
        if (renderer != null)
            renderer.enabled = false;

        rb.isKinematic = true;
        rb.position = hiddenPosition;
    }

    [Rpc(SendTo.Owner)]
    private void ConfirmPickupOwnerRpc()
    {
        playerInteraction = context.PlayerInteraction;
        ownerTransform = context.OwnerTransform;
        MakeViewModel(context.ViewModelContainer);
        playerInteraction.SetCurrentItem(this);

        context = null;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void DropClientRpc()
    {
        if (IsOwner) return;

        var renderer = GetMeshRenderer();
        if (renderer != null)
            renderer.enabled = true;

        rb.isKinematic = false;
    }

    // Server
    private void PickUpServer(ulong ownerId)
    {
        if (OwnerClientId != ownerId)
            GetComponent<NetworkObject>().ChangeOwnership(ownerId);

        HidePickupClientRpc();
        ConfirmPickupOwnerRpc();
    }

    // Client
    private MeshRenderer GetMeshRenderer()
    {
        if (meshRenderer == null)
            meshRenderer = GetComponentInChildren<MeshRenderer>();

        return meshRenderer;
    }

    private void MakeViewModel(Transform viewModelContainer)
    {
        viewModel = Instantiate(model, viewModelContainer);
        if (viewModel.transform.childCount > 0)
        {
            foreach (Transform child in viewModel.transform)
            {
                var childRenderer = child.GetComponent<MeshRenderer>();
                if (childRenderer == null) continue;

                childRenderer.renderingLayerMask = VIEWMODEL_RENDERING_LAYER_INDEX;
                childRenderer.enabled = true;
                child.gameObject.layer = VIEWMODEL_LAYER_INDEX;
            }
        }
        else
        {
            var viewModelRenderer = viewModel.GetComponent<MeshRenderer>();
            if (viewModelRenderer != null)
            {
                viewModelRenderer.renderingLayerMask = VIEWMODEL_RENDERING_LAYER_INDEX;
                viewModelRenderer.enabled = true;
            }
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
