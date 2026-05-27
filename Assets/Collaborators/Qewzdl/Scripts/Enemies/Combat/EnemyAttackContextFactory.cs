using Unity.Netcode;
using UnityEngine;

public sealed class EnemyAttackContextFactory
{
    public bool TryCreate(
        EnemyTarget target,
        EnemyConfig config,
        Vector3 attackerPosition,
        Component logContext,
        float maxDistance,
        out EnemyAttackContext context,
        out EnemyAttackResultType failureType
    )
    {
        context = default;
        failureType = EnemyAttackResultType.None;

        if (target == null || config == null)
        {
            failureType = EnemyAttackResultType.InvalidTarget;
            return false;
        }

        NetworkObject targetNetworkObject = target.NetworkObject;

        if (targetNetworkObject == null || !targetNetworkObject.IsSpawned)
        {
            failureType = EnemyAttackResultType.InvalidTarget;
            return false;
        }

        EnemyTargetIdentity targetIdentity = EnemyTargetIdentity.FromNetworkObject(targetNetworkObject);

        if (!targetIdentity.HasTarget)
        {
            failureType = EnemyAttackResultType.InvalidTarget;
            return false;
        }

        float effectiveMaxDistance = Mathf.Max(0f, maxDistance);

        float distanceToTarget = Vector3.Distance(
            attackerPosition,
            targetNetworkObject.transform.position
        );

        if (distanceToTarget > effectiveMaxDistance)
        {
            failureType = EnemyAttackResultType.OutOfRange;
            return false;
        }

        context = new EnemyAttackContext(
            target,
            targetIdentity,
            targetNetworkObject,
            config,
            attackerPosition,
            logContext
        );

        return true;
    }
}