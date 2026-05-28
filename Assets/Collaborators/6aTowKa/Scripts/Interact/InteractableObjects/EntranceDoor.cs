using System.Collections.Generic;
using UnityEngine;

public class EntranceDoor : InteractableObject
{
    private const int TotalHandles = 5;
    private HashSet<int> insertedHandles = new HashSet<int>();

    public override void Interact(InteractionContext context)
    {
        CheckDoor();
    }

    public bool InsertHandle(int handleId)
    {
        if (insertedHandles.Contains(handleId)) return false;

        insertedHandles.Add(handleId);
        Debug.Log($"Рукоятка #{handleId} вставлена. {insertedHandles.Count}/{TotalHandles}");
        return true;
    }

    private void CheckDoor()
    {
        if (insertedHandles.Count >= TotalHandles)
        {
            UnlockDoor();
        }
    }

    private void UnlockDoor()
    {
        Destroy(transform.parent.gameObject);
    }
}
