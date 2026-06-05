using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class EnemyVisionSensor : MonoBehaviour, IEnemyPerceptionSensor
{
    private const int MaxLineOfSightHits = 32;

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
    private readonly RaycastHit[] lineOfSightHits = new RaycastHit[MaxLineOfSightHits];
    private float nextOverflowWarningTime;
    private Vector3[] visibilityPointResults;

    private bool invalidStaticConfigurationLogged;
    private bool missingConfigLogged;

    public Transform OriginTransform => eyes;

    public bool IsConfigured =>
        ValidateStaticDependencies(false) &&
        ValidateRuntimeDependencies(false);

    public bool HasLastTargetedVerticalView { get; private set; }
    public Vector3 LastTargetedVerticalViewOrigin { get; private set; }
    public Vector3 LastTargetedVerticalViewCenterDirection { get; private set; }
    public float LastTargetedVerticalViewDistance { get; private set; }

    private void Awake()
    {
        EnsureDetectionBuffers();

        if (!ValidateStaticDependencies())
        {
            DisableUntilConfigured();
        }
    }

    public bool ValidateStaticDependencies()
    {
        return ValidateStaticDependencies(true);
    }

    public bool ValidateRuntimeDependencies()
    {
        return ValidateRuntimeDependencies(true);
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
        HasLastTargetedVerticalView = false;

        if (!ValidateRuntimeDependencies())
        {
            return null;
        }

        if (!ValidateConfig(config))
        {
            return null;
        }

        EnsureDetectionBuffers();

        Transform origin = eyes;

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

        if (target == null)
        {
            return false;
        }

        if (!ValidateRuntimeDependencies())
        {
            return false;
        }

        if (!ValidateConfig(config))
        {
            return false;
        }

        if (!target.TryGetVisibilityBounds(out Bounds targetBounds))
        {
            return false;
        }

        Transform origin = eyes;
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

        RememberLastTargetedVerticalView(originPosition, targetCenter, config.detectionRadius);

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
        if (!ValidateRuntimeDependencies())
        {
            return false;
        }

        Vector3 direction = targetPoint - originPosition;
        float distance = direction.magnitude;

        if (distance <= Mathf.Epsilon)
        {
            return true;
        }

        Ray lineOfSightRay = new(originPosition, direction / distance);

        if (HasDoorLineOfSightBlocker(lineOfSightRay, distance))
        {
            return false;
        }

        int hitCount = Physics.RaycastNonAlloc(
            originPosition,
            direction / distance,
            lineOfSightHits,
            distance,
            obstructionMask,
            QueryTriggerInteraction.Ignore
        );

        if (hitCount <= 0)
        {
            return true;
        }

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = lineOfSightHits[i].collider;

            if (hitCollider == null)
            {
                continue;
            }

            DoorInteractableObject door = hitCollider.GetComponentInParent<DoorInteractableObject>();

            if (door != null)
            {
                if (door.BlocksLineOfSight)
                {
                    return false;
                }

                continue;
            }

            if (IsLayerInMask(hitCollider.gameObject.layer, obstructionMask))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasDoorLineOfSightBlocker(Ray lineOfSightRay, float distance)
    {
        IReadOnlyList<DoorInteractableObject> doors = DoorInteractableObject.LineOfSightBlockers;

        for (int i = 0; i < doors.Count; i++)
        {
            DoorInteractableObject door = doors[i];

            if (door != null && door.TryBlockLineOfSight(lineOfSightRay, distance))
            {
                return true;
            }
        }

        return false;
    }

    private void RememberLastTargetedVerticalView(
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

        HasLastTargetedVerticalView = true;
        LastTargetedVerticalViewOrigin = originPosition;
        LastTargetedVerticalViewCenterDirection = direction.normalized;
        LastTargetedVerticalViewDistance = Mathf.Min(direction.magnitude, detectionRadius);
    }

    private bool ValidateConfig(EnemyConfig config)
    {
        return EnemyValidationLogger.ValidateConfig(
            this,
            nameof(EnemyVisionSensor),
            config,
            ref missingConfigLogged
        );
    }

    private bool ValidateStaticDependencies(bool logErrors)
    {
        StringBuilder builder = new();

        if (eyes == null)
        {
            EnemyValidationLogger.AppendMissingDependency(builder, nameof(eyes));
        }

        if (targetMask.value == 0)
        {
            EnemyValidationLogger.AppendEmptyLayerMask(builder, nameof(targetMask));
        }

        if (obstructionMask.value == 0)
        {
            EnemyValidationLogger.AppendEmptyLayerMask(builder, nameof(obstructionMask));
        }

        return EnemyValidationLogger.ValidateAndLog(
            this,
            nameof(EnemyVisionSensor),
            builder,
            ref invalidStaticConfigurationLogged,
            logErrors,
            "Vision sensor is disabled until configured."
        );
    }

    private bool ValidateRuntimeDependencies(bool logErrors)
    {
        return ValidateStaticDependencies(logErrors);
    }

    private void DisableUntilConfigured()
    {
        enabled = false;
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
        ValidateStaticDependencies();
    }

    private static bool IsLayerInMask(int layer, LayerMask layerMask)
    {
        return (layerMask.value & (1 << layer)) != 0;
    }
}
