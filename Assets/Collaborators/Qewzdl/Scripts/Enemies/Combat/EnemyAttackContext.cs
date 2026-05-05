using Unity.Netcode;
using UnityEngine;

public readonly struct EnemyAttackContext
{
    public EnemyTarget Target { get; }
    public EnemyTargetIdentity TargetIdentity { get; }
    public NetworkObject TargetNetworkObject { get; }
    public EnemyConfig Config { get; }
    public Vector3 AttackerPosition { get; }
    public Component Source { get; }

    public bool IsValid =>
        Target != null &&
        TargetIdentity.HasTarget &&
        TargetNetworkObject != null &&
        TargetNetworkObject.IsSpawned &&
        Config != null;

    public ulong TargetClientId => TargetIdentity.OwnerClientId;

    public string TargetDebugName
    {
        get
        {
            if (TargetNetworkObject == null)
            {
                return "None";
            }

            return $"{TargetNetworkObject.name} " +
                   $"(NetworkObjectId: {TargetNetworkObject.NetworkObjectId}, " +
                   $"OwnerClientId: {TargetIdentity.OwnerClientId})";
        }
    }

    public Vector3 TargetPosition
    {
        get
        {
            if (TryGetTargetNetworkObject(out NetworkObject networkObject))
            {
                return networkObject.transform.position;
            }

            return Target != null ? Target.transform.position : default;
        }
    }

    public EnemyAttackContext(
        EnemyTarget target,
        EnemyTargetIdentity targetIdentity,
        NetworkObject targetNetworkObject,
        EnemyConfig config,
        Vector3 attackerPosition,
        Component source
    )
    {
        Target = target;
        TargetIdentity = targetIdentity;
        TargetNetworkObject = targetNetworkObject;
        Config = config;
        AttackerPosition = attackerPosition;
        Source = source;
    }

    public bool TryGetTargetNetworkObject(out NetworkObject networkObject)
    {
        if (TargetIdentity.TryGetNetworkObject(out networkObject))
        {
            return true;
        }

        networkObject = TargetNetworkObject;
        return networkObject != null && networkObject.IsSpawned;
    }
}