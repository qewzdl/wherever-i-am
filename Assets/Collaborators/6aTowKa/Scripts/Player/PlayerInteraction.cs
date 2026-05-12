using UnityEngine;

public class PlayerInteraction : PlayerComponent, IPlayerSignalListener
{
    [SerializeField] private float interactionRange = 2f;
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private LayerMask interactableLayer;

    [SerializeField] private Sprite defualtCrosshair;
    [SerializeField] private Transform playerCameraTransform;
    [SerializeField] private PlayerController playerController;

    private InteractableObject currentInteractable;

    private InteractionContext interactionContext;
    private GameObject contactPoint;

    protected override void OnPostInit(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner)
    {
        signals.Interact.Listen(Interact);
        contactPoint = new GameObject();
    }

    public void Cleanup()
    {
        signals.Interact.Unlisten(Interact);
    }

    private void Raycast()
    {
        if (states.IsInteracting) return;

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
        interactionContext = new InteractionContext
        {
            HoldPoint = contactPoint.transform,
            PlayerCameraTransform = this.playerCameraTransform,
            PlayerController = this.playerController,
        };

        if (currentInteractable)
            currentInteractable.Interact(interactionContext);

        currentInteractable = null;

        signals.CrosshairSpriteSignal.Trigger(defualtCrosshair);
    }

    private void Update()
    {
        Raycast();
    }
}
