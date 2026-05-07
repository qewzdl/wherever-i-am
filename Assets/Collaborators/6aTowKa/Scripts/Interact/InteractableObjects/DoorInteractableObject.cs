using Unity.Netcode;
using UnityEngine;

public class DoorInteractableObject : InteractableObject
{
    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>();

    private bool isOpenLocal;

    private void OnEnable()
    {
        isOpen.OnValueChanged += Sync;
    }

    private void OnDisable()
    {
        isOpen.OnValueChanged -= Sync;
    }

    public override void Interact(InteractionContext context) 
    {
        SetIsOpen(!isOpenLocal);
        Door(isOpenLocal);
    }

    private void Sync(bool oldValue, bool newValue)
    {
        if (newValue == isOpenLocal) return;

        if (oldValue != newValue)
        { 
;           SetIsOpen(newValue);
            Door(newValue);
        }
    }

    private void SetIsOpen(bool value)
    {
        isOpenLocal = value;
        SetIsOpenRpc(value);
    }

    [Rpc(SendTo.Server)]
    private void SetIsOpenRpc(bool newValue)
    {
        isOpen.Value = newValue;
    }

    private void Door(bool value)
    {
        if (!value)
            transform.rotation = Quaternion.Euler(new Vector3(0, 0, 0));
        else
            transform.rotation = Quaternion.Euler(new Vector3(0, -90, 0));
    }

}
