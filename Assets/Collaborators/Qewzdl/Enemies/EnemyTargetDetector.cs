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

        Transform origin = GetOrigin();

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

        Transform origin = GetOrigin();

        Vector3 targetPoint = GetTargetPoint(target, config);
        Vector3 directionToTarget = targetPoint - origin.position;
        float distanceToTarget = directionToTarget.magnitude;

        if (distanceToTarget <= 0.001f)
        {
            return true;
        }

        if (distanceToTarget > config.detectionRadius)
        {
            return false;
        }

        float angle = Vector3.Angle(origin.forward, directionToTarget);

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

    private Transform GetOrigin()
    {
        return eyes != null ? eyes : transform;
    }

    private Vector3 GetTargetPoint(Transform target, EnemyConfig config)
    {
        return target.position + Vector3.up * config.targetHeightOffset;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        EnemyConfig config = GetDebugConfig();

        if (config == null)
        {
            return;
        }

        Transform origin = GetOrigin();

        DrawDetectionRadius(config);
        DrawViewAngle(config, origin);
        DrawEyesOrigin(origin);
    }

    private EnemyConfig GetDebugConfig()
    {
        NetworkEnemyController controller = GetComponent<NetworkEnemyController>();

        if (controller == null)
        {
            return null;
        }

        return controller.Config;
    }

    private void DrawDetectionRadius(EnemyConfig config)
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, config.detectionRadius);
    }

    private void DrawViewAngle(EnemyConfig config, Transform origin)
    {
        Vector3 forward = origin.forward;
        Vector3 leftDirection = Quaternion.AngleAxis(-config.viewAngle * 0.5f, Vector3.up) * forward;
        Vector3 rightDirection = Quaternion.AngleAxis(config.viewAngle * 0.5f, Vector3.up) * forward;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(origin.position, origin.position + forward * config.detectionRadius);
        Gizmos.DrawLine(origin.position, origin.position + leftDirection * config.detectionRadius);
        Gizmos.DrawLine(origin.position, origin.position + rightDirection * config.detectionRadius);
    }

    private void DrawEyesOrigin(Transform origin)
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(origin.position, 0.08f);
    }
#endif
}