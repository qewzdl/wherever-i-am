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

#if UNITY_EDITOR
    [Header("Debug")]
    [SerializeField] private bool drawLastTargetedVerticalView = true;
#endif

    private Collider[] detectionResults;
    private EnemyTarget[] processedTargets;
    private float nextOverflowWarningTime;
    private Vector3[] visibilityPointResults;

#if UNITY_EDITOR
    private bool hasLastVerticalViewDebug;
    private Vector3 lastVerticalViewOrigin;
    private Vector3 lastVerticalViewCenterDirection;
    private float lastVerticalViewDistance;
#endif

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

#if UNITY_EDITOR
        hasLastVerticalViewDebug = false;
#endif

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

        if (!target.TryGetVisibilityBounds(out Bounds targetBounds))
        {
            return false;
        }

        Transform origin = GetOrigin();
        Vector3 originPosition = origin.position;
        Vector3 targetCenter = targetBounds.center;
        Vector3 directionToTargetCenter = targetCenter - originPosition;

        if (!IsInsideDetectionRadius(directionToTargetCenter, config.detectionRadius))
        {
            return false;
        }

        if (!IsInsideHorizontalView(origin, directionToTargetCenter, config.horizontalViewAngle))
        {
            return false;
        }

#if UNITY_EDITOR
        RememberVerticalViewDebug(originPosition, targetCenter, config.detectionRadius);
#endif

        EnsureDetectionBuffers();

        int pointCount = target.GetVisibilityPointsNonAlloc(
            visibilityPointResults,
            config.targetHeightOffset
        );

        if (pointCount <= 0)
        {
            return false;
        }

        float verticalCenterAngle = GetVerticalAngleToPoint(originPosition, targetCenter);

        for (int i = 0; i < pointCount; i++)
        {
            Vector3 targetPoint = visibilityPointResults[i];

            if (!IsInsideTargetedVerticalView(
                originPosition,
                targetPoint,
                verticalCenterAngle,
                config.verticalViewAngle
            ))
            {
                continue;
            }

            if (!HasLineOfSight(originPosition, targetPoint))
            {
                continue;
            }

            visiblePoint = targetPoint;
            return true;
        }

        return false;
    }

    private bool IsInsideDetectionRadius(Vector3 directionToTarget, float detectionRadius)
    {
        return directionToTarget.sqrMagnitude <= detectionRadius * detectionRadius;
    }

    private bool IsInsideHorizontalView(
        Transform origin,
        Vector3 directionToTarget,
        float horizontalViewAngle
    )
    {
        Vector3 flatForward = Vector3.ProjectOnPlane(origin.forward, Vector3.up);
        Vector3 flatDirectionToTarget = Vector3.ProjectOnPlane(directionToTarget, Vector3.up);

        if (flatForward.sqrMagnitude <= 0.001f || flatDirectionToTarget.sqrMagnitude <= 0.001f)
        {
            return true;
        }

        float horizontalAngle = Vector3.Angle(flatForward, flatDirectionToTarget);
        return horizontalAngle <= horizontalViewAngle * 0.5f;
    }

    private bool IsInsideTargetedVerticalView(
        Vector3 originPosition,
        Vector3 targetPoint,
        float verticalCenterAngle,
        float verticalViewAngle
    )
    {
        float pointVerticalAngle = GetVerticalAngleToPoint(originPosition, targetPoint);
        float delta = Mathf.Abs(Mathf.DeltaAngle(verticalCenterAngle, pointVerticalAngle));

        return delta <= verticalViewAngle * 0.5f;
    }

    private float GetVerticalAngleToPoint(Vector3 originPosition, Vector3 point)
    {
        Vector3 direction = point - originPosition;
        Vector3 flatDirection = Vector3.ProjectOnPlane(direction, Vector3.up);

        float horizontalDistance = flatDirection.magnitude;
        float verticalDistance = direction.y;

        if (horizontalDistance <= 0.001f)
        {
            return verticalDistance >= 0f ? 90f : -90f;
        }

        return Mathf.Atan2(verticalDistance, horizontalDistance) * Mathf.Rad2Deg;
    }

    private bool HasLineOfSight(Vector3 originPosition, Vector3 targetPoint)
    {
        if (obstructionMask.value == 0)
        {
            Debug.LogWarning(
                $"{nameof(EnemyVisionSensor)} has empty obstruction mask. Enemy vision will ignore walls and cover.",
                this
            );

            return true;
        }

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
    private void RememberVerticalViewDebug(
        Vector3 originPosition,
        Vector3 targetCenter,
        float detectionRadius
    )
    {
        Vector3 direction = targetCenter - originPosition;

        if (direction.sqrMagnitude <= 0.001f)
        {
            return;
        }

        hasLastVerticalViewDebug = true;
        lastVerticalViewOrigin = originPosition;
        lastVerticalViewCenterDirection = direction.normalized;
        lastVerticalViewDistance = Mathf.Min(direction.magnitude, detectionRadius);
    }

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

        DrawHorizontalViewGizmos(origin, config);
        DrawForwardVerticalViewPreview(origin, config);

        if (drawLastTargetedVerticalView && hasLastVerticalViewDebug)
        {
            DrawTargetedVerticalViewGizmos(config);
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(origin.position, 0.08f);
    }

    private void DrawHorizontalViewGizmos(Transform origin, EnemyConfig config)
    {
        Vector3 forward = Vector3.ProjectOnPlane(origin.forward, Vector3.up);

        if (forward.sqrMagnitude <= 0.001f)
        {
            forward = transform.forward;
        }

        forward.Normalize();

        Vector3 leftDirection = Quaternion.AngleAxis(
            -config.horizontalViewAngle * 0.5f,
            Vector3.up
        ) * forward;

        Vector3 rightDirection = Quaternion.AngleAxis(
            config.horizontalViewAngle * 0.5f,
            Vector3.up
        ) * forward;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(origin.position, origin.position + forward * config.detectionRadius);
        Gizmos.DrawLine(origin.position, origin.position + leftDirection * config.detectionRadius);
        Gizmos.DrawLine(origin.position, origin.position + rightDirection * config.detectionRadius);
    }

    private void DrawForwardVerticalViewPreview(Transform origin, EnemyConfig config)
    {
        Vector3 forward = Vector3.ProjectOnPlane(origin.forward, Vector3.up);

        if (forward.sqrMagnitude <= 0.001f)
        {
            forward = transform.forward;
        }

        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward);

        if (right.sqrMagnitude <= 0.001f)
        {
            right = origin.right;
        }

        right.Normalize();

        DrawVerticalSlice(
            origin.position,
            forward,
            right,
            config.verticalViewAngle,
            config.detectionRadius,
            new Color(1f, 0.5f, 0f, 0.9f)
        );
    }

    private void DrawTargetedVerticalViewGizmos(EnemyConfig config)
    {
        Vector3 flatCenterDirection = Vector3.ProjectOnPlane(lastVerticalViewCenterDirection, Vector3.up);

        if (flatCenterDirection.sqrMagnitude <= 0.001f)
        {
            flatCenterDirection = transform.forward;
        }

        flatCenterDirection.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flatCenterDirection);

        if (right.sqrMagnitude <= 0.001f)
        {
            right = transform.right;
        }

        right.Normalize();

        DrawVerticalSlice(
            lastVerticalViewOrigin,
            lastVerticalViewCenterDirection,
            right,
            config.verticalViewAngle,
            lastVerticalViewDistance,
            Color.magenta
        );
    }

    private void DrawVerticalSlice(
        Vector3 originPosition,
        Vector3 centerDirection,
        Vector3 rightAxis,
        float verticalViewAngle,
        float distance,
        Color color
    )
    {
        if (centerDirection.sqrMagnitude <= 0.001f)
        {
            return;
        }

        centerDirection.Normalize();

        Vector3 upDirection = Quaternion.AngleAxis(
            -verticalViewAngle * 0.5f,
            rightAxis
        ) * centerDirection;

        Vector3 downDirection = Quaternion.AngleAxis(
            verticalViewAngle * 0.5f,
            rightAxis
        ) * centerDirection;

        Gizmos.color = color;
        Gizmos.DrawLine(originPosition, originPosition + centerDirection * distance);
        Gizmos.DrawLine(originPosition, originPosition + upDirection * distance);
        Gizmos.DrawLine(originPosition, originPosition + downDirection * distance);
    }
#endif
}