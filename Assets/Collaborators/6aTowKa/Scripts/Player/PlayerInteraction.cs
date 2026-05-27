using UnityEngine;

public class PlayerInteraction : PlayerComponent, IPlayerSignalListener
{
    // Interaction settings
    [SerializeField] private float interactionRange = 2f;
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private LayerMask interactableLayer;

    // Objects
    [SerializeField] private Sprite defualtCrosshair;
    [SerializeField] private Transform playerCameraTransform;
    [SerializeField] private PlayerController playerController;

    // Items
    [SerializeField] private Transform viewModelContainer;
    [SerializeField] private Transform itemDropTransform;

    private InteractableObject currentInteractable;
    private Item currentItem;

    private InteractionContext interactionContext;
    private PickUpContext pickUpContext;
    private GameObject contactPoint;

    protected override void OnPostInit(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner)
    {
        contactPoint = new GameObject();
        contactPoint.transform.parent = this.transform;
        contactPoint.name = "ContactPoint";

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
    }

    private void Raycast()
    {
        if (states.IsDragging) return;

        Debug.DrawRay(rayOrigin.position, rayOrigin.forward * interactionRange, Color.red);
        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, interactionRange, interactableLayer))
        {
            InteractableObject interactable = hit.collider.GetComponentInParent<InteractableObject>();

            if (interactable)
            {
                if (currentInteractable != interactable)
                {
                    currentInteractable = interactable;
                    if (states.IsCarring && currentInteractable is Object draggingObject) return;
                    signals.CrosshairSpriteSignal.Trigger(interactable.InteractionSprite);
                }

                if (interactable is Object)
                    contactPoint.transform.position = hit.point;

                return;
            }
        }

        if (currentInteractable != null)
        {
            currentInteractable = null;
            signals.CrosshairSpriteSignal.Trigger(defualtCrosshair);
        }
    }

    private void Interact()
    {
        if (states.IsDragging) return;

        if (states.IsCarring)
        {
            bool success = currentItem.Action();
            if (success) return;
        }

        interactionContext = new InteractionContext
        {
            HoldPoint = contactPoint.transform,
            PlayerCameraTransform = this.playerCameraTransform,
            PlayerController = this.playerController,
            RayOriginPosition = rayOrigin.position,
            currentPlayerItem = currentItem,
        };

        if (currentInteractable is DraggingObject draggingObject)
        {
            if (states.IsCarring) return;
            draggingObject.Interact(interactionContext);
            states.IsDragging = true;
        }
        else if (currentInteractable)
        {
            currentInteractable.Interact(interactionContext);
            currentInteractable = null;
        }

        signals.CrosshairSpriteSignal.Trigger(defualtCrosshair);
    }

    private void Uninteract()
    {
        if (currentInteractable is DraggingObject draggingObject)
        {
            draggingObject.Uninteract();
            currentInteractable = null;
        }

        states.IsDragging = false;
    }

    private void PickUp()
    {
        if (states.IsDragging) return;
        if (currentInteractable == null) return;

        pickUpContext = new PickUpContext
        {
            ViewModelContainer = this.viewModelContainer,
            OwnerTransform = itemDropTransform
        };

        if (currentInteractable is Item item)
        {
            item.PickUp(pickUpContext);
            states.IsCarring = true;
            currentItem = item;
        }

    }

    private void Drop()
    {
        if (!currentItem) return;
        currentItem.Drop();
        currentItem = null;
        states.IsCarring = false;
    }

    private void Update()
    {
        Raycast();
    }
}
