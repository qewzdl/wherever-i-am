using Unity.Netcode;
using UnityEngine;

public abstract class InteractableObject : NetworkBehaviour
{
    [SerializeField] public Sprite InteractionSprite;
    public abstract void Interact();
}