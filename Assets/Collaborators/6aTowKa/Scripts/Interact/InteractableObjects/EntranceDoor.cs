using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class EntranceDoor : InteractableObject
{
    private const int TotalHandles = 5;

    private readonly HashSet<int> insertedHandles = new HashSet<int>();
    private bool isUnlocked;

    public event Action<int, int, int, ulong> HandleInserted;
    public event Action<ulong> Unlocked;

    public int InsertedHandleCount => insertedHandles.Count;
    public int TotalHandleCount => TotalHandles;
    public bool IsUnlocked => isUnlocked;

    public override void Interact(InteractionContext context)
    {
        RequestDoorUnlock();
    }

    public bool InsertHandle(int handleId)
    {
        if (IsServer || !IsSpawned)
        {
            return TryInsertHandle(handleId, GetLocalClientId());
        }

        if (insertedHandles.Contains(handleId))
        {
            return false;
        }

        InsertHandleServerRpc(handleId);
        return true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void InsertHandleServerRpc(int handleId, RpcParams rpcParams = default)
    {
        TryInsertHandle(handleId, rpcParams.Receive.SenderClientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestDoorUnlockServerRpc(RpcParams rpcParams = default)
    {
        TryUnlockDoor(rpcParams.Receive.SenderClientId);
    }

    private bool TryInsertHandle(int handleId, ulong instigatorClientId)
    {
        if (isUnlocked || insertedHandles.Contains(handleId))
        {
            return false;
        }

        insertedHandles.Add(handleId);

        Debug.Log($"Entrance door handle #{handleId} inserted. {insertedHandles.Count}/{TotalHandles}", this);
        HandleInserted?.Invoke(handleId, insertedHandles.Count, TotalHandles, instigatorClientId);
        return true;
    }

    private void RequestDoorUnlock()
    {
        if (IsServer || !IsSpawned)
        {
            TryUnlockDoor(GetLocalClientId());
            return;
        }

        RequestDoorUnlockServerRpc();
    }

    private bool TryUnlockDoor(ulong instigatorClientId)
    {
        if (isUnlocked || insertedHandles.Count < TotalHandles)
        {
            return false;
        }

        isUnlocked = true;
        Unlocked?.Invoke(instigatorClientId);

        GameObject target = transform.parent != null ? transform.parent.gameObject : gameObject;
        Destroy(target);
        return true;
    }

    private ulong GetLocalClientId()
    {
        return NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
    }
}
