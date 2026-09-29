using System;
using System.Collections.Generic;
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
    private PickUpContext context;
    private Renderer[] shownRenderers;
    private Collider[] colliders;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;

    public bool IsPickedUp => netIsPickedUp.Value;

    // Set by whatever used the item up - a door that took the handle - so
    // the item may then be destroyed at its carrier's request, and not
    // before. Server only.
    public bool IsConsumedServer { get; private set; }

    public void MarkConsumedServer()
    {
        if (IsServer)
            IsConsumedServer = true;
    }

    // The item with this id that this client is actually holding, as the
    // server sees it. What a client says it holds is not evidence of it.
    public static bool TryFindHeldServer(
        NetworkManager manager,
        ulong clientId,
        int itemId,
        out PickupItem held)
    {
        held = null;

        if (manager == null ||
            !manager.IsServer ||
            !manager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
        {
            return false;
        }

        foreach (NetworkObject owned in client.OwnedObjects)
        {
            if (owned != null &&
                owned.IsSpawned &&
                owned.TryGetComponent(out PickupItem item) &&
                item.IsPickedUp &&
                item.GetItemID() == itemId)
            {
                held = item;
                return true;
            }
        }

        return false;
    }
    public event Action<bool> PickedUpChanged;

    protected override void Awake()
    {
        base.Awake();

        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        netIsPickedUp.OnValueChanged += HandlePickedUpChanged;
    }

    private void OnValidate()
    {
        if (data != null)
        {
            if (data is not PickupItemData)
                Debug.LogError($"Data for {name} must be of type PickupItemData!", this);
        }
    }

    protected override bool IsHeld => base.IsHeld || netIsPickedUp.Value;

    // Put down where its carrier stood (see DraggableObject.ReleaseServer), or
    // back where it was found when nobody saw where that was.
    protected override void LetGoServer(ulong holderClientId, Vector3? dropPosition)
    {
        if (!netIsPickedUp.Value)
        {
            base.LetGoServer(holderClientId, dropPosition);
            return;
        }

        PlayerActionGateContext.TryEnd(
            NetworkManager,
            holderClientId,
            PlayerActionKind.Pickup,
            this);
        netIsPickedUp.Value = false;

        // Here as well as in the message to everybody: a server that is not
        // also a player is sent nothing, and would keep the item switched
        // off where it was picked up.
        SetCarried(false);

        rb.position = dropPosition ?? spawnPosition;
        rb.rotation = dropPosition.HasValue ? Quaternion.identity : spawnRotation;

        // Put down means put down at rest. The body kept whatever it was
        // doing before somebody picked it up, and would carry on doing it the
        // moment it was dynamic again.
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    // Shown again everywhere; on the carrier's machine the model leaves their
    // hand and they stop carrying.
    protected override void ForgetLocalHold()
    {
        SetCarried(false);

        context?.PlayerInteraction?.HandlePickupUnavailable(this);

        if (playerInteraction != context?.PlayerInteraction)
            playerInteraction?.HandlePickupUnavailable(this);

        if (viewModel != null)
            Destroy(viewModel);

        context = null;
        ownerTransform = null;
        base.ForgetLocalHold();
    }

    public override void OnNetworkDespawn()
    {
        netIsPickedUp.OnValueChanged -= HandlePickedUpChanged;

        if (IsServer && netIsPickedUp.Value)
        {
            PlayerActionGateContext.TryEnd(
                NetworkManager,
                OwnerClientId,
                PlayerActionKind.Pickup,
                this);
        }

        base.OnNetworkDespawn();
    }

    private void HandlePickedUpChanged(bool _, bool isPickedUp)
    {
        PickedUpChanged?.Invoke(isPickedUp);
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
        if (NetworkManager == null)
        {
            context?.PlayerInteraction?.DenyPickup();
            return;
        }

        this.context = context;
        context.PlayerInteraction.RequestPickup(this);
    }

    protected void Drop()
    {
        if (ownerTransform == null) return;

        SetCarried(false);

        rb.rotation = Quaternion.identity;
        rb.position = ownerTransform.position;

        Destroy(viewModel);

        DropServerRpc();
        ownerTransform = null;

        playerInteraction.SetIsCarrying(false);
    }

    // Server RPCs

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void DropServerRpc(RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        // The owner's own client refuses this before asking, so reaching here
        // while hidden means a client that does not. The gate is the server's
        // own answer to what that player is doing - a carry stands aside for
        // the hiding and is not the action in progress - so it is the one
        // worth trusting.
        if (PlayerActionGateContext.TryGet(
                NetworkManager,
                senderClientId,
                out IPlayerActionGate actionGate) &&
            actionGate.IsActive(PlayerActionKind.Hiding))
        {
            return;
        }

        netIsPickedUp.Value = false;
        PlayerActionGateContext.TryEnd(
            NetworkManager,
            senderClientId,
            PlayerActionKind.Pickup,
            this);
        DropClientRpc();
    }

    internal bool TryPickUpServer(ulong ownerId)
    {
        if (!IsServer ||
            netIsPickedUp.Value ||
            netIsDragging.Value ||
            !PlayerActionGateContext.TryBegin(
                NetworkManager,
                ownerId,
                PlayerActionKind.Pickup,
                this,
                out IPlayerActionGate actionGate))
        {
            return false;
        }

        try
        {
            BeginHoldServer(ownerId);
            netIsPickedUp.Value = true;
            PickUpServer(ownerId);
            return true;
        }
        catch
        {
            actionGate.End(PlayerActionKind.Pickup, this);
            throw;
        }
    }

    internal void ClearPendingPickup(PlayerInteraction requester)
    {
        if (context?.PlayerInteraction == requester)
            context = null;
    }

    // Carried means out of sight and out of the way. It used to also mean a
    // thousand metres underground: every instance teleported the body there
    // while somebody held it.
    //
    // That was a position written by instances that do not own the position.
    // These items sync through a NetworkTransform with owner authority, so a
    // non-owner writing rb.position is writing something the network is about
    // to overwrite, and which of the two lands last is a matter of frame
    // timing. It showed up as an item that came back a few centimetres off its
    // spawn point, or a thousand metres under it, or an ownership handover
    // that never arrived - three faces of the same race.
    //
    // Turning the renderer and the colliders off does everything the teleport
    // was for. Nothing sees it, nothing walks into it, and nobody writes a
    // position they do not own.
    private void SetCarried(bool carried)
    {
        Renderer[] renderers = GetShownRenderers();

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = !carried;
        }

        colliders ??= GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = !carried;
        }

        rb.isKinematic = carried;
    }

    // Client RPCs

    [Rpc(SendTo.ClientsAndHost)]
    private void HidePickupClientRpc()
    {
        SetCarried(true);
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

        SetCarried(false);
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
    // Every part of the item that is drawn, not just the first. An item made
    // of several meshes - the crumpled paper is five - left all but one of
    // them hanging in the air where it was picked up. Only the ones drawn to
    // begin with, so hiding and showing never turns on a part that was meant
    // to stay off.
    private Renderer[] GetShownRenderers()
    {
        if (shownRenderers != null)
            return shownRenderers;

        Renderer[] all = GetComponentsInChildren<Renderer>(true);
        List<Renderer> shown = new(all.Length);

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].enabled)
                shown.Add(all[i]);
        }

        shownRenderers = shown.ToArray();
        return shownRenderers;
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
