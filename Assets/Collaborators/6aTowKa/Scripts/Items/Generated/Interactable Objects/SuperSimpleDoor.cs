using Unity.Netcode;
using UnityEngine;

public class SuperSimpleDoor : InteractableObject
{
    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>
    (
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Vector3 closedRotation;
    private Vector3 openRotation;

    private void Awake()
    {
        closedRotation = transform.rotation.eulerAngles;
        openRotation = transform.rotation.eulerAngles + new Vector3(0, 90, 0);
    }

    public override void OnInteract(InteractionContext context)
    {
        UpdateDoorRotationRpc();
    }

    [Rpc(SendTo.Server)]
    private void UpdateDoorRotationRpc()
    {
        isOpen.Value = !isOpen.Value;
        if (isOpen.Value)
            transform.rotation = Quaternion.Euler(openRotation);
        else
            transform.rotation = Quaternion.Euler(closedRotation);
    }
}
