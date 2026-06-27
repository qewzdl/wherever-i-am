using UnityEngine;

[CreateAssetMenu(fileName = "NewInteractableObjectData", menuName = "Wherever I Am/Items/Draggable Object data")]
public class DraggableObjectData : InteractableObjectData
{
    public float Mass = 1f;
    public float MaxFollowSpeed = 15f;
    [Range(0f, 20f)] public float FollowSpeedMultiplier = 2f;
    public float MaxDragDistance = 6f;
    public float ThrowVelocitySamples = 5f;
    public float MinDistance = 1f;
}
