using UnityEngine;

public class PlayerInteraction : PlayerComponent, IPlayerSignalListener
{
    // Interaction settings
    [SerializeField] private float interactionRange = 2f;
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private LayerMask interactableLayer;

    // Objects
    [SerializeField] private Sprite defaultCrosshair;
    [SerializeField] private Transform playerCameraTransform;
    [SerializeField] private PlayerController playerController;

    // Items
    [SerializeField] private Transform viewModelContainer;
    [SerializeField] private Transform itemDropTransform;

    private InteractableObject focusedInteractable;
    private PickupItem currentItem;
    private DraggableObject currentDraggable;
    
    private GameObject HitPoint;
    private bool crosshairIsDefualt;

    private RaycastHit hit;

    protected override void OnPostInit(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner)
    {
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
        signals.Interact.Unlisten(Interact);
        signals.Uninteract.Unlisten(Uninteract);
        signals.PickUp.Unlisten(PickUp);
        signals.Drop.Unlisten(Drop);

        if (HitPoint != null)
            Destroy(HitPoint);
    }

    private void Update()
    {
        Raycast();
    }

    // Focusing
    private void Raycast()
    {
        if (!CanFocus()) return;

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
        if (states.IsDragging) return false;
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
            HitPoint.transform.position = hit.point;

            draggingObject.OnInteract(ctx);
            states.IsDragging = true;
            currentDraggable = draggingObject;
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
            RayOriginPosition = rayOrigin.position,
            CurrentItem = currentItem,
            PlayerInteraction = this,
        };
    }
    //Undragging
    public void Undrag()
    {
        if (currentDraggable != null)
        {
            currentDraggable.OnUninteract();
            states.IsDragging = false;
            currentDraggable = null;
        }
    }

    // Picking up and dropping items
    private void PickUp()
    {
        if (focusedInteractable is not PickupItem item) return;
        if (!CanPickUp()) return;

        PickUpContext pickUpContext = BuildPickUpContext();

        item.OnPickup(pickUpContext);
        SetCurrentItem(item);
    }

    private void Drop()
    {
        if (!currentItem) return;

        currentItem.OnDrop();
        SetCurrentItem(null);
    }

    private bool CanPickUp()
    {
        if (states.IsCarrying) return false;
        if (states.IsDragging) return false;
        return true;
    }


    private void SetCurrentItem(PickupItem item)
    {
        currentItem = item;
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
