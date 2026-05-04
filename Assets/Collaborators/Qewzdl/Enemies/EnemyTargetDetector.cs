using Unity.Netcode;
using UnityEngine;

public class EnemyTargetDetector : MonoBehaviour
{
    [SerializeField] private Transform eyes;
    [SerializeField] private LayerMask playerMask;
    [SerializeField] private LayerMask obstructionMask;

    public Transform FindBestVisibleTarget(EnemyConfig config)
    {
        if (config == null)
        {
            return null;
        }

        Transform origin = eyes != null ? eyes : transform;

        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            config.detectionRadius,
            playerMask,
            QueryTriggerInteraction.Ignore
        );

        Transform bestTarget = null;
        float bestDistanceSqr = float.MaxValue;

        foreach (Collider hit in hits)
        {
            if (hit == null)
            {
                continue;
            }

            NetworkObject networkObject = hit.GetComponentInParent<NetworkObject>();

            if (networkObject == null || !networkObject.IsSpawned)
            {
                continue;
            }

            Transform candidate = networkObject.transform;
            Vector3 targetPoint = GetTargetPoint(candidate, config);
            Vector3 toCandidate = targetPoint - origin.position;
            float distanceSqr = toCandidate.sqrMagnitude;

            if (distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            if (!CanSeeTarget(candidate, config))
            {
                continue;
            }

            bestTarget = candidate;
            bestDistanceSqr = distanceSqr;
        }

        return bestTarget;
    }

    public bool CanSeeTarget(Transform target, EnemyConfig config)
    {
        if (target == null || config == null)
        {
            return false;
        }

        Transform origin = eyes != null ? eyes : transform;
        Vector3 targetPoint = GetTargetPoint(target, config);
        Vector3 directionToTarget = targetPoint - origin.position;
        float distanceToTarget = directionToTarget.magnitude;

        if (distanceToTarget > config.detectionRadius)
        {
            return false;
        }

        float angle = Vector3.Angle(transform.forward, directionToTarget);

        if (angle > config.viewAngle * 0.5f)
        {
            return false;
        }

        if (obstructionMask.value == 0)
        {
            return true;
        }

        bool blocked = Physics.Raycast(
            origin.position,
            directionToTarget.normalized,
            distanceToTarget,
            obstructionMask,
            QueryTriggerInteraction.Ignore
        );

        return !blocked;
    }

    private Vector3 GetTargetPoint(Transform target, EnemyConfig config)
    {
        return target.position + Vector3.up * config.targetHeightOffset;
    }
}