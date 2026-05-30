using UnityEngine;

public abstract class ItemRequiredInteractable : InteractableObject
{
    [SerializeField] int requiredItemID;

    public override void OnInteract(InteractionContext context)
    {
        PickupItem currentItem = context.CurrentItem;

        if (currentItem != null)
        {
            if (currentItem is IActivator activator)
            {
                if (currentItem.GetItemID() == requiredItemID)
                {
                    InteractWith(activator);
                }
                else
                    UnsuccessfulInteract();
            }
            else
            {
                Debug.LogWarning("Current item does not implement IActivator interface.");
                UnsuccessfulInteract();
            }

        }
        else
            UnsuccessfulInteract();


    }

    protected abstract void InteractWith(IActivator activator);

    protected abstract void UnsuccessfulInteract();
}
