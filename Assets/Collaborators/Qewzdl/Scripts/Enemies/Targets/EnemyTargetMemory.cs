using Unity.Netcode;
using UnityEngine;

public sealed class EnemyTargetMemory
{
    public const ulong NoTargetClientId = ulong.MaxValue;

    public EnemyTarget CurrentTarget { get; private set; }
    public ulong CurrentTargetClientId { get; private set; } = NoTargetClientId;
    public Vector3 LastKnownTargetPosition { get; private set; }
    public bool HasLastKnownTargetPosition { get; private set; }

    public bool HasTarget => CurrentTarget != null;

    public bool IsCurrentTargetValid
    {
        get
        {
            return CurrentTarget != null && CurrentTarget.IsValidNetworkTarget;
        }
    }

    public void SetTarget(EnemyTarget target, Vector3 position)
    {
        CurrentTarget = target;
        CurrentTargetClientId = GetClientId(target);
        LastKnownTargetPosition = position;
        HasLastKnownTargetPosition = true;
    }

    public void ClearTargetOnly()
    {
        CurrentTarget = null;
        CurrentTargetClientId = NoTargetClientId;
    }

    public void ClearAll()
    {
        ClearTargetOnly();
        LastKnownTargetPosition = default;
        HasLastKnownTargetPosition = false;
    }

    public static ulong GetClientId(EnemyTarget target)
    {
        if (target == null)
        {
            return NoTargetClientId;
        }

        NetworkObject targetNetworkObject = target.NetworkObject;

        if (targetNetworkObject == null || !targetNetworkObject.IsSpawned)
        {
            return NoTargetClientId;
        }

        return targetNetworkObject.OwnerClientId;
    }
}