using UnityEngine;

[DisallowMultipleComponent]
public class EnemyVisionSensor : MonoBehaviour, IEnemyPerceptionSensor
{
    [SerializeField] private Transform eyes;
    [SerializeField] private LayerMask playerMask;
    [SerializeField] private LayerMask obstructionMask;

    [Header("Performance")]
    [SerializeField, Min(1)] private int maxDetectionResults = 16;
    [SerializeField, Min(0.1f)] private float overflowWarningCooldown = 2f;

    private Collider[] detectionResults;
    private EnemyTarget[] processedTargets;
    private float nextOverflowWarningTime;

    private void Awake()
    {
        EnsureDetectionBuffers();
    }

    public bool TryFindBestStimulus(EnemyConfig config, out EnemyPerceptionStimulus stimulus)
    {
        stimulus = EnemyPerceptionStimulus.None;

        EnemyTarget bestTarget = FindBestVisibleTarget(config, out float bestScore);

        if (bestTarget == null)
        {
            return false;
        }

        stimulus = EnemyPerceptionStimulus.ForConfirmedTarget(
            bestTarget,
            bestTarget.AimPosition,
            bestScore,
            EnemyPerceptionSource.Vision
        );

        return true;
    }

    public EnemyTarget FindBestVisibleTarget(EnemyConfig config)
    {
        return FindBestVisibleTarget(config, out _);
    }

    private EnemyTarget FindBestVisibleTarget(EnemyConfig config, out float bestScore)
    {
        bestScore = 0f;

        if (config == null)
        {
            return null;
        }

        EnsureDetectionBuffers();

        Transform origin = GetOrigin();

        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            config.detectionRadius,
            detectionResults,
            playerMask,
            QueryTriggerInteraction.Ignore
        );

        WarnIfDetectionBufferIsFull(hitCount);

        EnemyTarget bestTarget = null;
        float bestDistanceSqr = float.MaxValue;
        int processedTargetCount = 0;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = detectionResults[i];

            if (hit == null)
            {
                continue;
            }

            EnemyTarget enemyTarget = hit.GetComponentInParent<EnemyTarget>();

            if (enemyTarget == null)
            {
                continue;
            }

            if (!enemyTarget.CanBeDetected || !enemyTarget.IsValidNetworkTarget)
            {
                continue;
            }

            if (HasProcessedTarget(enemyTarget, processedTargetCount))
            {
                continue;
            }

            processedTargets[processedTargetCount] = enemyTarget;
            processedTargetCount++;

            Vector3 targetPoint = enemyTarget.AimPosition;
            Vector3 toCandidate = targetPoint - origin.position;
            float distanceSqr = toCandidate.sqrMagnitude;

            if (distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            if (!CanSeeTarget(enemyTarget, config))
            {
                continue;
            }

            bestTarget = enemyTarget;
            bestDistanceSqr = distanceSqr;
        }

        ClearProcessedTargets(processedTargetCount);

        if (bestTarget == null)
        {
            return null;
        }

        float distance = Mathf.Sqrt(bestDistanceSqr);
        bestScore = 1f / Mathf.Max(0.001f, distance);

        return bestTarget;
    }

    private bool CanSeeTarget(EnemyTarget target, EnemyConfig config)
    {
        if (target == null || config == null)
        {
            return false;
        }

        Transform origin = GetOrigin();

        Vector3 targetPoint = target.AimPosition;
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

    private void EnsureDetectionBuffers()
    {
        int safeSize = Mathf.Max(1, maxDetectionResults);

        if (detectionResults == null || detectionResults.Length != safeSize)
        {
            detectionResults = new Collider[safeSize];
        }

        if (processedTargets == null || processedTargets.Length != safeSize)
        {
            processedTargets = new EnemyTarget[safeSize];
        }
    }

    private bool HasProcessedTarget(EnemyTarget target, int processedTargetCount)
    {
        for (int i = 0; i < processedTargetCount; i++)
        {
            if (processedTargets[i] == target)
            {
                return true;
            }
        }

        return false;
    }

    private void ClearProcessedTargets(int processedTargetCount)
    {
        for (int i = 0; i < processedTargetCount; i++)
        {
            processedTargets[i] = null;
        }
    }

    private void WarnIfDetectionBufferIsFull(int hitCount)
    {
        if (hitCount < detectionResults.Length)
        {
            return;
        }

        if (Time.unscaledTime < nextOverflowWarningTime)
        {
            return;
        }

        nextOverflowWarningTime = Time.unscaledTime + overflowWarningCooldown;

        Debug.LogWarning(
            $"{nameof(EnemyVisionSensor)} reached max detection results ({detectionResults.Length}). " +
            $"Increase {nameof(maxDetectionResults)} if targets can be missed.",
            this
        );
    }

    private Transform GetOrigin()
    {
        return eyes != null ? eyes : transform;
    }

#if UNITY_EDITOR
    private void Reset()
    {
        EnsureDetectionBuffers();
    }

    private void OnValidate()
    {
        maxDetectionResults = Mathf.Max(1, maxDetectionResults);
        overflowWarningCooldown = Mathf.Max(0.1f, overflowWarningCooldown);
        EnsureDetectionBuffers();
    }

    private void OnDrawGizmosSelected()
    {
        NetworkEnemyController controller = GetComponent<NetworkEnemyController>();
        EnemyConfig config = controller != null ? controller.Config : null;

        if (config == null)
        {
            return;
        }

        Transform origin = GetOrigin();

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, config.detectionRadius);

        Vector3 forward = origin.forward;
        Vector3 leftDirection = Quaternion.AngleAxis(-config.viewAngle * 0.5f, Vector3.up) * forward;
        Vector3 rightDirection = Quaternion.AngleAxis(config.viewAngle * 0.5f, Vector3.up) * forward;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(origin.position, origin.position + forward * config.detectionRadius);
        Gizmos.DrawLine(origin.position, origin.position + leftDirection * config.detectionRadius);
        Gizmos.DrawLine(origin.position, origin.position + rightDirection * config.detectionRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(origin.position, 0.08f);
    }
#endif
}