using System;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EntranceDoor : InteractableObject
{
    [Header("Handle Requirements")]
    [SerializeField] private bool requireHandleItem = true;
    [SerializeField] private int requiredHandleItemId;
    [SerializeField] [Min(1)] private int requiredHandleCount = 1;
    [SerializeField] private bool consumeInsertedHandle = true;

    [Header("Door Visual")]
    [SerializeField] private Transform doorPivot;
    [SerializeField] private bool useLocalRotation = true;
    [SerializeField] private Vector3 lockedEulerAngles;
    [SerializeField] private Vector3 unlockedEulerAngles = new(0f, 90f, 0f);

    [Header("Network")]
    [SerializeField] private bool startsUnlocked;
    [SerializeField] private bool despawnNetworkObjectWhenUnlocked;
    [SerializeField] private NetworkObject networkObjectToDespawn;

    private readonly NetworkVariable<int> insertedHandleCountNetwork = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<bool> isUnlockedNetwork = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private int localInsertedHandleCount;
    private bool localIsUnlocked;

    public event Action<int, int, int, ulong> HandleInserted;
    public event Action<ulong> Unlocked;

    public bool IsUnlocked => IsSpawned ? isUnlockedNetwork.Value : localIsUnlocked;
    public int InsertedHandleCount => IsSpawned ? insertedHandleCountNetwork.Value : localInsertedHandleCount;
    public int RequiredHandleCount => Mathf.Max(1, requiredHandleCount);

    private void Awake()
    {
        CacheComponents();

        localIsUnlocked = startsUnlocked;
        ApplyUnlockedState(localIsUnlocked);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        isUnlockedNetwork.OnValueChanged += HandleUnlockedChanged;

        if (IsServer)
        {
            isUnlockedNetwork.Value = startsUnlocked;
            insertedHandleCountNetwork.Value = startsUnlocked ? RequiredHandleCount : 0;
        }

        localIsUnlocked = isUnlockedNetwork.Value;
        localInsertedHandleCount = insertedHandleCountNetwork.Value;
        ApplyUnlockedState(localIsUnlocked);
    }

    public override void OnNetworkDespawn()
    {
        isUnlockedNetwork.OnValueChanged -= HandleUnlockedChanged;

        base.OnNetworkDespawn();
    }

    public override void OnInteract(InteractionContext context)
    {
        if (IsUnlocked)
        {
            return;
        }

        int handleId = requiredHandleItemId;
        IActivator activator = null;

        if (requireHandleItem && !TryResolveHandleItem(context, out handleId, out activator))
        {
            return;
        }

        if (IsSpawned && !IsServer)
        {
            RequestInsertHandleRpc(handleId);

            if (consumeInsertedHandle)
            {
                activator?.Activate();
            }

            return;
        }

        if (TryInsertHandleAuthoritative(handleId, ResolveLocalClientId()) && consumeInsertedHandle)
        {
            activator?.Activate();
        }
    }

    public bool TryInsertHandle(int handleId, ulong instigatorClientId = 0)
    {
        if (IsSpawned && !IsServer)
        {
            RequestInsertHandleRpc(handleId);
            return true;
        }

        return TryInsertHandleAuthoritative(handleId, instigatorClientId);
    }

    public bool TryUnlock(ulong instigatorClientId = 0)
    {
        if (IsSpawned && !IsServer)
        {
            return false;
        }

        return UnlockAuthoritative(instigatorClientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestInsertHandleRpc(int handleId, RpcParams rpcParams = default)
    {
        TryInsertHandleAuthoritative(handleId, rpcParams.Receive.SenderClientId);
    }

    private bool TryInsertHandleAuthoritative(int handleId, ulong instigatorClientId)
    {
        if (IsUnlocked)
        {
            return false;
        }

        if (requireHandleItem && handleId != requiredHandleItemId)
        {
            Debug.LogWarning(
                $"{nameof(EntranceDoor)} rejected handle item id {handleId}; expected {requiredHandleItemId}.",
                this
            );

            return false;
        }

        int nextHandleCount = Mathf.Min(InsertedHandleCount + 1, RequiredHandleCount);
        SetInsertedHandleCount(nextHandleCount);

        HandleInserted?.Invoke(handleId, nextHandleCount, RequiredHandleCount, instigatorClientId);

        if (nextHandleCount >= RequiredHandleCount)
        {
            UnlockAuthoritative(instigatorClientId);
        }

        return true;
    }

    private bool UnlockAuthoritative(ulong instigatorClientId)
    {
        if (IsUnlocked)
        {
            return false;
        }

        SetUnlocked(true);
        Unlocked?.Invoke(instigatorClientId);

        if (despawnNetworkObjectWhenUnlocked && IsSpawned && IsServer && networkObjectToDespawn != null)
        {
            networkObjectToDespawn.Despawn(false);
        }

        return true;
    }

    private bool TryResolveHandleItem(
        InteractionContext context,
        out int handleId,
        out IActivator activator
    )
    {
        handleId = requiredHandleItemId;
        activator = null;

        PickupItem currentItem = context?.CurrentItem;

        if (currentItem == null)
        {
            Debug.LogWarning($"{nameof(EntranceDoor)} requires a handle item.", this);
            return false;
        }

        if (currentItem is not IActivator itemActivator)
        {
            Debug.LogWarning($"{nameof(EntranceDoor)} handle item must implement {nameof(IActivator)}.", this);
            return false;
        }

        handleId = currentItem.GetItemID();

        if (handleId != requiredHandleItemId)
        {
            Debug.LogWarning(
                $"{nameof(EntranceDoor)} received item id {handleId}; expected {requiredHandleItemId}.",
                this
            );

            return false;
        }

        activator = itemActivator;
        return true;
    }

    private void SetInsertedHandleCount(int value)
    {
        localInsertedHandleCount = Mathf.Clamp(value, 0, RequiredHandleCount);

        if (IsSpawned && IsServer)
        {
            insertedHandleCountNetwork.Value = localInsertedHandleCount;
        }
    }

    private void SetUnlocked(bool value)
    {
        localIsUnlocked = value;

        if (IsSpawned && IsServer)
        {
            isUnlockedNetwork.Value = value;
        }

        ApplyUnlockedState(value);
    }

    private void HandleUnlockedChanged(bool previousValue, bool nextValue)
    {
        localIsUnlocked = nextValue;
        ApplyUnlockedState(nextValue);
    }

    private void ApplyUnlockedState(bool value)
    {
        Transform pivot = doorPivot != null ? doorPivot : transform;
        Quaternion rotation = Quaternion.Euler(value ? unlockedEulerAngles : lockedEulerAngles);

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

        if (networkObjectToDespawn == null)
        {
            networkObjectToDespawn = GetComponentInParent<NetworkObject>();
        }
    }

    private ulong ResolveLocalClientId()
    {
        return NetworkManager != null
            ? NetworkManager.LocalClientId
            : Unity.Netcode.NetworkManager.ServerClientId;
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        CacheComponents();
        requiredHandleCount = Mathf.Max(1, requiredHandleCount);
    }
#endif
}
