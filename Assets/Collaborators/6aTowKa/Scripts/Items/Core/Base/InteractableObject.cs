using Unity.Netcode;
using UnityEngine;

public abstract class InteractableObject : NetworkBehaviour
{
    [SerializeField] protected InteractableObjectData data;

    public abstract void OnInteract(InteractionContext context); //Entry point for all interactable objects for button E!!!

    public Sprite GetIteractionSprite()
    {
        return data.InteractionSprite;
    }
}
