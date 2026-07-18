using UnityEngine;

public class InteractionContext
{
    public Transform HitPoint;
    public Vector3 RayOriginPosition;
    public Transform PlayerCameraTransform;
    public PlayerController PlayerController;
    public PlayerHidingController PlayerHidingController;
    public PickupItem CurrentItem;
    public PlayerInteraction PlayerInteraction;
}

