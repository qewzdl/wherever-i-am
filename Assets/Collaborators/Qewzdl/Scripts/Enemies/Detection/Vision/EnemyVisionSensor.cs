using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class EnemyVisionSensor : MonoBehaviour, IEnemyPerceptionSensor
{
    [SerializeField] private Transform eyes;

    [FormerlySerializedAs("playerMask")]
    [SerializeField] private LayerMask targetMask;

    [SerializeField] private LayerMask obstructionMask;

    [Header("Performance")]
    [SerializeField, Min(1)] private int maxDetectionResults = 16;
    [SerializeField, Min(0.1f)] private float overflowWarningCooldown = 2f;

    [Header("Visibility")]
    [SerializeField, Min(1)] private int maxVisibilityPoints = 8;

    private Collider[] detectionResults;
    private EnemyTarget[] processedTargets;
    private float nextOverflowWarningTime;
    private Vector3[] visibilityPointResults;

    private void Awake()
    {
        EnsureDetectionBuffers();
    }

    public bool TryFindBestStimulus(EnemyConfig config, out EnemyPerceptionStimulus stimulus)
    {
        stimulus = EnemyPerceptionStimulus.None;

        EnemyTarget bestTarget = FindBestVisibleTarget(config, out float bestScore, out Vector3 visiblePoint);

        if (bestTarget == null)
        {
            return false;
        }

        stimulus = EnemyPerceptionStimulus.ForConfirmedTarget(
            bestTarget,
            visiblePoint,
            bestScore,
            EnemyPerceptionSource.Vision
        );

        return true;
    }

    public EnemyTarget FindBestVisibleTarget(EnemyConfig config)
    {
        return FindBestVisibleTarget(config, out _, out _);
    }

    private EnemyTarget FindBestVisibleTarget(
        EnemyConfig config,
        out float bestScore,
        out Vector3 bestVisiblePoint
    )
    {
        bestScore = 0f;
        bestVisiblePoint = Vector3.zero;

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
            targetMask,
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

            if (!CanSeeTarget(enemyTarget, config, out Vector3 visiblePoint))
            {
                continue;
            }

            Vector3 toCandidate = visiblePoint - origin.position;
            float distanceSqr = toCandidate.sqrMagnitude;

            if (distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            bestTarget = enemyTarget;
            bestDistanceSqr = distanceSqr;
            bestVisiblePoint = visiblePoint;
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
        return CanSeeTarget(target, config, out _);
    }

    private bool CanSeeTarget(EnemyTarget target, EnemyConfig config, out Vector3 visiblePoint)
    {
        visiblePoint = Vector3.zero;

        if (target == null || config == null)
        {
            return false;
        }

        EnsureDetectionBuffers();

        int pointCount = target.GetVisibilityPointsNonAlloc(
            visibilityPointResults,
            config.targetHeightOffset
        );

        if (pointCount <= 0)
        {
            return false;
        }

        Transform origin = GetOrigin();

        for (int i = 0; i < pointCount; i++)
        {
            Vector3 targetPoint = visibilityPointResults[i];

            if (CanSeePoint(origin, targetPoint, config))
            {
                visiblePoint = targetPoint;
                return true;
            }
        }

        return false;
    }

    private bool CanSeePoint(Transform origin, Vector3 targetPoint, EnemyConfig config)
    {
        Vector3 originPosition = origin.position;
        Vector3 directionToTarget = targetPoint - originPosition;
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
            Debug.LogWarning(
                $"{nameof(EnemyVisionSensor)} has empty obstruction mask. Enemy vision will ignore walls and cover.",
                this
            );

            return true;
        }

        Vector3 direction = directionToTarget / distanceToTarget;

        bool blocked = Physics.Linecast(
            originPosition,
            targetPoint,
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

        int safeVisibilityPointCount = Mathf.Max(1, maxVisibilityPoints);

        if (visibilityPointResults == null || visibilityPointResults.Length != safeVisibilityPointCount)
        {
            visibilityPointResults = new Vector3[safeVisibilityPointCount];
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
        maxVisibilityPoints = Mathf.Max(maxVisibilityPoints, 8);
        EnsureDetectionBuffers();
    }

    private void OnValidate()
    {
        maxDetectionResults = Mathf.Max(1, maxDetectionResults);
        overflowWarningCooldown = Mathf.Max(0.1f, overflowWarningCooldown);
        maxVisibilityPoints = Mathf.Max(1, maxVisibilityPoints);
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