using UnityEngine;

public class DoorHandleCase : InteractableObject
{
    [SerializeField] private EntranceDoor entranceDoor;

    public override void Interact(InteractionContext context)
    {
        if (context.currentPlayerItem == null) return;

        if (context.currentPlayerItem is EntranceDoorHandle entranceDoorHandle)
        {
            bool success = entranceDoor.InsertHandle(entranceDoorHandle.HandleID);
            if (success) Destroy(gameObject);
            entranceDoorHandle.Use();
        }
    }
}
