using UnityEngine;

public class PlayerInteraction : PlayerComponent, IPlayerSignalListener
{
    [SerializeField] private float interactionRange = 2f;
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private Sprite defualtCrosshair;
    [SerializeField] private LayerMask interactableLayer;

    private InteractableObject currentInteractable;

    protected override void OnPostInit(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner)
    {
        signals.Interact.Listen(Interact);
    }

    public void Cleanup()
    {
        signals.Interact.Unlisten(Interact);
    }

    private void Raycast()
    {
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
                    return;
                }
                else
                {
                    return;
                }
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
        if (currentInteractable)
            currentInteractable.Interact();
    }

    private void Update()
    {
        Raycast();
    }
}
