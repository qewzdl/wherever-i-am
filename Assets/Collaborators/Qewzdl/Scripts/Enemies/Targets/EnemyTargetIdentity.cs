using System;
using Unity.Netcode;

public struct EnemyTargetIdentity : INetworkSerializable, IEquatable<EnemyTargetIdentity>
{
    public static readonly EnemyTargetIdentity None = new(
        false,
        default,
        EnemyTargetMemory.NoTargetClientId
    );

    private bool hasTarget;
    private NetworkObjectReference targetObject;
    private ulong ownerClientId;

    public bool HasTarget => hasTarget;
    public NetworkObjectReference TargetObject => targetObject;
    public ulong OwnerClientId => ownerClientId;

    public EnemyTargetIdentity(
        bool hasTarget,
        NetworkObjectReference targetObject,
        ulong ownerClientId
    )
    {
        this.hasTarget = hasTarget;
        this.targetObject = targetObject;
        this.ownerClientId = ownerClientId;
    }

    public static EnemyTargetIdentity FromTarget(EnemyTarget target)
    {
        if (target == null)
        {
            return None;
        }

        return FromNetworkObject(target.NetworkObject);
    }

    public static EnemyTargetIdentity FromNetworkObject(NetworkObject networkObject)
    {
        if (networkObject == null || !networkObject.IsSpawned)
        {
            return None;
        }

        return new EnemyTargetIdentity(
            true,
            new NetworkObjectReference(networkObject),
            networkObject.OwnerClientId
        );
    }

    public bool TryGetNetworkObject(out NetworkObject networkObject)
    {
        networkObject = null;

        if (!hasTarget)
        {
            return false;
        }

        return targetObject.TryGet(out networkObject) &&
               networkObject != null &&
               networkObject.IsSpawned;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref hasTarget);
        serializer.SerializeValue(ref targetObject);
        serializer.SerializeValue(ref ownerClientId);
    }

    public bool Equals(EnemyTargetIdentity other)
    {
        return hasTarget == other.hasTarget &&
               ownerClientId == other.ownerClientId &&
               targetObject.Equals(other.targetObject);
    }

    public override bool Equals(object obj)
    {
        return obj is EnemyTargetIdentity other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = hasTarget ? 17 : 31;
            hash = hash * 23 + targetObject.GetHashCode();
            hash = hash * 23 + ownerClientId.GetHashCode();
            return hash;
        }
    }

    public static bool operator ==(EnemyTargetIdentity left, EnemyTargetIdentity right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(EnemyTargetIdentity left, EnemyTargetIdentity right)
    {
        return !left.Equals(right);
    }
}