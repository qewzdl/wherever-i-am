using UnityEngine;

public sealed class EnemyLineOfHitValidator
{
    private const int MaxHits = 16;

    private readonly RaycastHit[] hits = new RaycastHit[MaxHits];

    public bool TryValidate(
        EnemyAttackContext context,
        out EnemyAttackResultType failureType
    )
    {
        failureType = EnemyAttackResultType.None;

        if (!context.IsValid)
        {
            failureType = EnemyAttackResultType.InvalidTarget;
            return false;
        }

        EnemyConfig config = context.Config;

        if (!config.attackLineOfHitValidationEnabled)
        {
            return true;
        }

        if (config.attackLineOfHitBlockingMask.value == 0)
        {
            Debug.LogError(
                $"{nameof(EnemyLineOfHitValidator)} requires a non-empty " +
                $"{nameof(config.attackLineOfHitBlockingMask)} when line-of-hit validation is enabled.",
                context.Source
            );

            failureType = EnemyAttackResultType.LineOfHitBlocked;
            return false;
        }

        Vector3 origin = context.AttackerPosition +
                         Vector3.up * config.attackLineOfHitOriginHeight;

        Vector3 targetPosition = context.Target.AimPosition;
        Vector3 direction = targetPosition - origin;

        float distance = direction.magnitude;

        if (distance <= Mathf.Epsilon)
        {
            return true;
        }

        direction /= distance;

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            direction,
            hits,
            distance,
            config.attackLineOfHitBlockingMask,
            config.attackLineOfHitTriggerInteraction
        );

        if (hitCount <= 0)
        {
            return true;
        }

        Transform attackerRoot = context.Source != null
            ? context.Source.transform
            : null;

        Transform targetRoot = context.TargetNetworkObject != null
            ? context.TargetNetworkObject.transform
            : context.Target.transform;

        float closestBlockingDistance = float.PositiveInfinity;
        bool hasBlockingHit = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hits[i];

            if (hit.collider == null)
            {
                continue;
            }

            Transform hitTransform = hit.collider.transform;

            if (IsSameHierarchy(hitTransform, attackerRoot))
            {
                continue;
            }

            if (IsSameHierarchy(hitTransform, targetRoot))
            {
                continue;
            }

            if (hit.distance < closestBlockingDistance)
            {
                closestBlockingDistance = hit.distance;
                hasBlockingHit = true;
            }
        }

        if (!hasBlockingHit)
        {
            return true;
        }

        failureType = EnemyAttackResultType.LineOfHitBlocked;
        return false;
    }

    private bool IsSameHierarchy(Transform candidate, Transform root)
    {
        if (candidate == null || root == null)
        {
            return false;
        }

        return candidate == root || candidate.IsChildOf(root);
    }
}