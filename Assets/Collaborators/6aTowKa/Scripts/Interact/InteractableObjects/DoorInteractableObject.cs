using Unity.Netcode;
using UnityEngine;

public class DoorInteractableObject : InteractableObject
{
    [SerializeField] private Transform doorPivot;
    [SerializeField] private bool startsOpen;
    [SerializeField] private Vector3 closedEulerAngles;
    [SerializeField] private Vector3 openEulerAngles = new(0f, -90f, 0f);
    [SerializeField] private bool useLocalRotation;

    private readonly NetworkVariable<bool> isOpen = new();

    private bool isOpenLocal;

    public bool IsOpen => IsSpawned ? isOpen.Value : isOpenLocal;

    private void Awake()
    {
        CacheComponents();
        isOpenLocal = startsOpen;
        ApplyOpenState(isOpenLocal);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            isOpen.Value = startsOpen;
        }

        isOpen.OnValueChanged += Sync;
        ApplyOpenState(isOpen.Value);
    }

    public override void OnNetworkDespawn()
    {
        isOpen.OnValueChanged -= Sync;

        base.OnNetworkDespawn();
    }

    public override void OnInteract(InteractionContext context)
    {
        TrySetOpen(!IsOpen);
    }

    public bool TrySetOpen(bool value)
    {
        if (IsOpen == value)
        {
            return false;
        }

        RequestOpenState(value);
        return true;
    }

    private void Sync(bool oldValue, bool newValue)
    {
        ApplyOpenState(newValue);
    }

    private void RequestOpenState(bool value)
    {
        if (!IsSpawned || IsServer)
        {
            SetOpenStateServer(value);
            return;
        }

        SetOpenStateRpc(value);
    }

    private void SetOpenStateServer(bool value)
    {
        if (IsSpawned)
        {
            isOpen.Value = value;
        }

        ApplyOpenState(value);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetOpenStateRpc(bool newValue)
    {
        SetOpenStateServer(newValue);
    }

    private void ApplyOpenState(bool value)
    {
        isOpenLocal = value;
        ApplyDoorVisual(value);
    }

    private void ApplyDoorVisual(bool value)
    {
        Transform pivot = doorPivot != null ? doorPivot : transform;
        Quaternion rotation = Quaternion.Euler(value ? openEulerAngles : closedEulerAngles);

        if (useLocalRotation)
        {
            pivot.localRotation = rotation;
            return;
        }

        pivot.rotation = rotation;
    }

    private void CacheComponents()
    {
        if (doorPivot == null)
        {
            doorPivot = transform;
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        CacheComponents();
    }
#endif
}
