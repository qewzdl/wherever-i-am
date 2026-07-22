using UnityEngine;

public class InteractionContext
{
    public Transform HitPoint;
    public Vector3 RayOriginPosition;
    public Transform PlayerCameraTransform;
    public PlayerController PlayerController;
    public IPlayerHidingCommandService PlayerHidingCommands;
    public IPlayerActionGate PlayerActionGate;
    public PickupItem CurrentItem;
    public PlayerInteraction PlayerInteraction;
}

