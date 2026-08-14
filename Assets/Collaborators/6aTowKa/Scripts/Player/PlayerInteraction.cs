using Unity.Netcode;
using UnityEngine;

public class PlayerInteraction : PlayerNetworkComponent, IPlayerSignalListener
{
    // Interaction settings
    [SerializeField] private float interactionRange = 2f;
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private LayerMask interactableLayer;

    // Objects
    [SerializeField] private Sprite defaultCrosshair;
    [SerializeField] private Transform playerCameraTransform;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private MonoBehaviour playerHidingCommandSource;
    [SerializeField] private MonoBehaviour playerActionGateSource;

    // Items
    [SerializeField] private Transform viewModelContainer;
    [SerializeField] private Transform itemDropTransform;

    private InteractableObject focusedInteractable;
    private PickupItem currentItem;
    private DraggableObject currentDraggable;
    private PickupItem pendingPickup;
    private DraggableObject pendingDraggable;

    private IPlayerHidingCommandService playerHidingCommands;
    private IPlayerActionGate playerActionGate;

    private bool dragRequestPending;
    private bool pickupRequestPending;
    private bool hasLocalControl;

    private GameObject HitPoint;
    private bool crosshairIsDefualt;

    private RaycastHit hit;

    protected override void OnPostInit(PlayerOrchestrator orch)
    {
        bool isMultiplayer =
            IsSpawned && NetworkManager != null && NetworkManager.IsListening;
        hasLocalControl = !isMultiplayer || IsOwner;

        if (!hasLocalControl)
            return;

        ResolvePlayerServices();

        HitPoint = new GameObject();
        HitPoint.transform.parent = transform;
        HitPoint.name = "ContactPoint";

        signals.Interact.Listen(Interact);
        signals.Uninteract.Listen(Uninteract);
        signals.PickUp.Listen(PickUp);
        signals.Drop.Listen(Drop);
    }

    public void Cleanup()
    {
        if (!hasLocalControl)
            return;

        ReleaseInteractionActions();

        if (signals != null)
        {
            signals.Interact.Unlisten(Interact);
            signals.Uninteract.Unlisten(Uninteract);
            signals.PickUp.Unlisten(PickUp);
            signals.Drop.Unlisten(Drop);
        }

        if (HitPoint != null)
            Destroy(HitPoint);
    }

    private void Update()
    {
        if (!hasLocalControl)
            return;

        Raycast();
    }

    // Focusing
    private void Raycast()
    {
        if (!CanFocus())
        {
            if (focusedInteractable != null || !crosshairIsDefualt)
                ResetFocusedInteractable();

            return;
        }

        Debug.DrawRay(rayOrigin.position, rayOrigin.forward * interactionRange, Color.red);
        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);

        if (Physics.Raycast(ray, out hit, interactionRange, interactableLayer))
        {
            InteractableObject interactable = hit.collider.GetComponentInParent<InteractableObject>();

            if (interactable)
            {
                if (focusedInteractable != interactable)
                    if (CanFocusOn(interactable))
                        SetFocusedInteractable(interactable);
            }
        }
        else
        {
            if (focusedInteractable != null || !crosshairIsDefualt)
                ResetFocusedInteractable();
        }
    }

    private void SetFocusedInteractable(InteractableObject interactable)
    {
        focusedInteractable = interactable;
        signals.CrosshairSpriteSignal.Trigger(interactable.GetIteractionSprite());
        crosshairIsDefualt = false;
    }

    private void ResetFocusedInteractable()
    {
        focusedInteractable = null;
        signals.CrosshairSpriteSignal.Trigger(defaultCrosshair);
        crosshairIsDefualt = true;
    }

    private bool CanFocus()
    {
        if (playerActionGate != null &&
            (playerActionGate.IsActive(PlayerActionKind.Hiding) ||
             playerActionGate.IsActive(PlayerActionKind.Drag))) return false;
        if (dragRequestPending) return false;
        return true;
    }

    private bool CanFocusOn(InteractableObject interactable)
    {
        if (states.IsCarrying && interactable is DraggableObject and not ItemInteractableDraggable) return false;

        return true;
    }

    //Interacting
    private void Interact()
    {
        if (playerActionGate != null &&
            playerActionGate.IsActive(PlayerActionKind.Hiding))
        {
            playerHidingCommands?.RequestExitHiding();
            return;
        }

        if (currentItem != null && focusedInteractable == null)
        {
            if (currentItem is UsableItem usableItem and not ActivatableUsableItem)
            {
                usableItem.Use();
                return;
            }
        }

        if (focusedInteractable != null)
            InteractWithWorldInteractable();
    }

    private void InteractWithWorldInteractable()
    {
        InteractionContext ctx = BuildInteractionContext();

        if (focusedInteractable is DraggableObject draggingObject)
        {
            if (playerActionGate == null ||
                !playerActionGate.TryBegin(
                    PlayerActionKind.Drag,
                    draggingObject))
            {
                return;
            }

            HitPoint.transform.position = hit.point;

            pendingDraggable = draggingObject;
            dragRequestPending = true;
            draggingObject.OnInteract(ctx);
        }
        else
        {
            focusedInteractable.OnInteract(ctx);
        }
    }

    private void Uninteract()
    {
        Undrag();
    }

    private InteractionContext BuildInteractionContext()
    {
        return new InteractionContext
        {
            HitPoint = HitPoint.transform,
            PlayerCameraTransform = playerCameraTransform,
            PlayerController = playerController,
            PlayerHidingCommands = playerHidingCommands,
            PlayerActionGate = playerActionGate,
            RayOriginPosition = rayOrigin.position,
            CurrentItem = currentItem,
            PlayerInteraction = this,
        };
    }

    public void RequestDrag(DraggableObject target)
    {
        if (!hasLocalControl || !IsSpawned || target == null || !target.IsSpawned)
        {
            DenyDragging();
            return;
        }

        RequestDragServerRpc(target.NetworkObject);
    }

    public void RequestPickup(PickupItem target)
    {
        if (!hasLocalControl || !IsSpawned || target == null || !target.IsSpawned)
        {
            DenyPickup();
            return;
        }

        RequestPickupServerRpc(target.NetworkObject);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestDragServerRpc(NetworkObjectReference targetReference)
    {
        if (!targetReference.TryGet(out NetworkObject targetObject, NetworkManager) ||
            targetObject.GetComponent<DraggableObject>() is not DraggableObject target ||
            !CanReach(target) ||
            !target.TryStartDraggingServer(OwnerClientId))
        {
            DenyDraggingOwnerRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestPickupServerRpc(NetworkObjectReference targetReference)
    {
        if (!targetReference.TryGet(out NetworkObject targetObject, NetworkManager) ||
            targetObject.GetComponent<PickupItem>() is not PickupItem target ||
            !CanReach(target) ||
            !target.TryPickUpServer(OwnerClientId))
        {
            DenyPickupOwnerRpc();
        }
    }

    private bool CanReach(DraggableObject target)
    {
        if (!IsServer || target == null || rayOrigin == null || target.Colliders == null)
            return false;

        Vector3 origin = rayOrigin.position;
        float maxDistanceSqr = interactionRange * interactionRange;
        int lineOfSightMask =
            Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Player");

        foreach (Collider targetCollider in target.Colliders)
        {
            if (targetCollider == null ||
                !targetCollider.enabled ||
                !targetCollider.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector3 offset = targetCollider.ClosestPoint(origin) - origin;

            if (offset.sqrMagnitude > maxDistanceSqr)
                continue;

            float distance = offset.magnitude;

            if (distance < 0.001f)
                return true;

            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                offset / distance,
                distance + 0.02f,
                lineOfSightMask,
                QueryTriggerInteraction.Ignore);
            RaycastHit closestHit = default;
            float closestDistance = float.PositiveInfinity;

            foreach (RaycastHit candidate in hits)
            {
                NetworkObject hitNetworkObject =
                    candidate.collider.GetComponentInParent<NetworkObject>();

                if (hitNetworkObject != null &&
                    hitNetworkObject.NetworkManager != NetworkManager)
                {
                    continue;
                }

                if (candidate.distance >= closestDistance)
                    continue;

                closestHit = candidate;
                closestDistance = candidate.distance;
            }

            if (closestDistance == float.PositiveInfinity ||
                closestHit.collider.GetComponentInParent<DraggableObject>() == target)
            {
                return true;
            }
        }

        return false;
    }

    [Rpc(SendTo.Owner)]
    private void DenyDraggingOwnerRpc()
    {
        DenyDragging();
    }

    [Rpc(SendTo.Owner)]
    private void DenyPickupOwnerRpc()
    {
        DenyPickup();
    }

    //Undragging
    public void Undrag()
    {
        if (currentDraggable != null)
        {
            DraggableObject draggable = currentDraggable;
            currentDraggable = null;
            states.IsDragging = false;
            playerActionGate?.End(PlayerActionKind.Drag, draggable);

            draggable.OnUninteract();
        }
    }

    // Picking up and dropping items
    private void PickUp()
    {
        if (focusedInteractable is not PickupItem item) return;
        if (!CanPickUp()) return;
        if (playerActionGate == null ||
            !playerActionGate.TryBegin(PlayerActionKind.Pickup, item)) return;

        PickUpContext pickUpContext = BuildPickUpContext();

        pendingPickup = item;
        pickupRequestPending = true;
        item.OnPickup(pickUpContext);
    }

    private void Drop()
    {
        if (!currentItem) return;

        currentItem.OnDrop();
        SetCurrentItem(null);
    }

    private bool CanPickUp()
    {
        if (pickupRequestPending) return false;
        return playerActionGate != null &&
               playerActionGate.CanBegin(
                   PlayerActionKind.Pickup,
                   focusedInteractable);
    }


    public void SetCurrentItem(PickupItem item)
    {
        PickupItem previousItem = currentItem;
        pickupRequestPending = false;
        pendingPickup = null;
        currentItem = item;

        if (item != null)
        {
            playerActionGate?.Confirm(PlayerActionKind.Pickup, item);
        }
        else if (previousItem != null)
        {
            playerActionGate?.End(
                PlayerActionKind.Pickup,
                previousItem);
        }

        states.IsCarrying = item != null;
    }

    private PickUpContext BuildPickUpContext()
    {
        return new PickUpContext
        {
            ViewModelContainer = viewModelContainer,
            OwnerTransform = itemDropTransform,
            PlayerInteraction = this,
        };
    }

    //other
    public void SetIsCarrying(bool value)
    {
        states.IsCarrying = value;
    }

    public void ConfirmDragging(DraggableObject draggable)
    {
        dragRequestPending = false;
        pendingDraggable = null;
        currentDraggable = draggable;
        playerActionGate?.Confirm(PlayerActionKind.Drag, draggable);
        states.IsDragging = true;
    }

    public void DenyDragging()
    {
        DraggableObject rejected = pendingDraggable;
        dragRequestPending = false;
        pendingDraggable = null;

        if (rejected != null)
            playerActionGate?.End(PlayerActionKind.Drag, rejected);
    }

    public void DenyPickup()
    {
        PickupItem rejected = pendingPickup;
        pickupRequestPending = false;
        pendingPickup = null;

        if (rejected != null)
        {
            rejected.ClearPendingPickup(this);
            playerActionGate?.End(PlayerActionKind.Pickup, rejected);
        }
    }

    public void HandleDraggableUnavailable(DraggableObject draggable)
    {
        if (draggable == null)
            return;

        if (pendingDraggable == draggable)
            DenyDragging();

        if (currentDraggable != draggable)
            return;

        currentDraggable = null;
        states.IsDragging = false;
        playerActionGate?.End(PlayerActionKind.Drag, draggable);
    }

    public void HandlePickupUnavailable(PickupItem item)
    {
        if (item == null)
            return;

        if (pendingPickup == item)
            DenyPickup();

        if (currentItem != item)
            return;

        currentItem = null;
        states.IsCarrying = false;
        playerActionGate?.End(PlayerActionKind.Pickup, item);
    }

    public bool CanEnterHiding()
    {
        return playerActionGate != null &&
               playerHidingCommands != null &&
               playerActionGate.CanBegin(
                   PlayerActionKind.Hiding,
                   playerHidingCommands) &&
               !dragRequestPending &&
               !pickupRequestPending;
    }

    public void SetHidingActive(bool value)
    {
        if (!value)
            return;

        if (pendingDraggable != null)
            playerActionGate?.End(
                PlayerActionKind.Drag,
                pendingDraggable);

        if (pendingPickup != null)
            playerActionGate?.End(
                PlayerActionKind.Pickup,
                pendingPickup);

        pendingDraggable = null;
        pendingPickup = null;
        dragRequestPending = false;
        pickupRequestPending = false;

        if (signals != null &&
            (focusedInteractable != null || !crosshairIsDefualt))
            ResetFocusedInteractable();
    }

    private void ResolvePlayerServices()
    {
        playerHidingCommands =
            playerHidingCommandSource as IPlayerHidingCommandService;
        playerActionGate = playerActionGateSource as IPlayerActionGate;

        MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();

        for (int i = 0;
             i < behaviours.Length &&
             (playerHidingCommands == null || playerActionGate == null);
             i++)
        {
            MonoBehaviour behaviour = behaviours[i];

            if (playerHidingCommands == null &&
                behaviour is IPlayerHidingCommandService hidingCommands)
            {
                playerHidingCommandSource = behaviour;
                playerHidingCommands = hidingCommands;
            }

            if (playerActionGate == null &&
                behaviour is IPlayerActionGate actionGate)
            {
                playerActionGateSource = behaviour;
                playerActionGate = actionGate;
            }
        }
    }

    private void ReleaseInteractionActions()
    {
        if (playerActionGate == null)
            return;

        if (pendingDraggable != null)
            playerActionGate.End(PlayerActionKind.Drag, pendingDraggable);
        if (currentDraggable != null)
            playerActionGate.End(PlayerActionKind.Drag, currentDraggable);
        if (pendingPickup != null)
            playerActionGate.End(PlayerActionKind.Pickup, pendingPickup);
        if (currentItem != null)
            playerActionGate.End(PlayerActionKind.Pickup, currentItem);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolvePlayerServices();
    }
#endif

    //debug
    private void OnDrawGizmos()
    {
        if (hit.point != Vector3.zero)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(hit.point, 0.06f);
        }
    }
}
