using Unity.Netcode;
using UnityEngine;

public readonly struct EnemyAttackContext
{
    public EnemyTarget Target { get; }
    public NetworkObject TargetNetworkObject { get; }
    public EnemyConfig Config { get; }
    public Vector3 AttackerPosition { get; }
    public Component Source { get; }

    public bool IsValid =>
        Target != null &&
        TargetNetworkObject != null &&
        TargetNetworkObject.IsSpawned &&
        Config != null;

    public ulong TargetClientId =>
        TargetNetworkObject != null && TargetNetworkObject.IsSpawned
            ? TargetNetworkObject.OwnerClientId
            : EnemyTargetMemory.NoTargetClientId;

    public Vector3 TargetPosition
    {
        get
        {
            if (TargetNetworkObject != null && TargetNetworkObject.IsSpawned)
            {
                return TargetNetworkObject.transform.position;
            }

            return Target != null ? Target.transform.position : default;
        }
    }

    public EnemyAttackContext(
        EnemyTarget target,
        NetworkObject targetNetworkObject,
        EnemyConfig config,
        Vector3 attackerPosition,
        Component source
    )
    {
        Target = target;
        TargetNetworkObject = targetNetworkObject;
        Config = config;
        AttackerPosition = attackerPosition;
        Source = source;
    }
}