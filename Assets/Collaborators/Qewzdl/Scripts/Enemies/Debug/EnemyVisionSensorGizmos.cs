using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyVisionSensor))]
public class EnemyVisionSensorGizmos : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyVisionSensor visionSensor;
    [SerializeField] private NetworkEnemyController enemyController;

    [Header("Drawing")]
    [SerializeField] private bool drawOnlyWhenSelected = true;
    [SerializeField] private bool drawDetectionRadius = true;
    [SerializeField] private bool drawHorizontalView = true;
    [SerializeField] private bool drawForwardVerticalViewPreview = true;
    [SerializeField] private bool drawLastTargetedVerticalView = true;
    [SerializeField] private float originRadius = 0.08f;

    [Header("Colors")]
    [SerializeField] private Color detectionRadiusColor = Color.red;
    [SerializeField] private Color horizontalViewColor = Color.yellow;
    [SerializeField] private Color forwardVerticalViewColor = new(1f, 0.5f, 0f, 0.9f);
    [SerializeField] private Color targetedVerticalViewColor = Color.magenta;
    [SerializeField] private Color originColor = Color.cyan;

    private void Awake()
    {
        CacheComponents();
    }

    private void OnDrawGizmos()
    {
        if (drawOnlyWhenSelected)
        {
            return;
        }

        DrawGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawOnlyWhenSelected)
        {
            return;
        }

        DrawGizmos();
    }

    private void DrawGizmos()
    {
        CacheComponents();

        if (visionSensor == null || !TryGetConfig(out EnemyConfig config))
        {
            return;
        }

        Transform origin = visionSensor.OriginTransform;

        if (origin == null)
        {
            return;
        }

        if (drawDetectionRadius)
        {
            Gizmos.color = detectionRadiusColor;
            Gizmos.DrawWireSphere(transform.position, config.detectionRadius);
        }

        if (drawHorizontalView)
        {
            DrawHorizontalView(origin, config);
        }

        if (drawForwardVerticalViewPreview)
        {
            DrawForwardVerticalView(origin, config);
        }

        if (drawLastTargetedVerticalView && visionSensor.HasLastTargetedVerticalView)
        {
            DrawTargetedVerticalView(config);
        }

        Gizmos.color = originColor;
        Gizmos.DrawSphere(origin.position, Mathf.Max(0.01f, originRadius));
    }

    private void DrawHorizontalView(Transform origin, EnemyConfig config)
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

        Gizmos.color = horizontalViewColor;
        Gizmos.DrawLine(origin.position, origin.position + forward * config.detectionRadius);
        Gizmos.DrawLine(origin.position, origin.position + leftDirection * config.detectionRadius);
        Gizmos.DrawLine(origin.position, origin.position + rightDirection * config.detectionRadius);
    }

    private void DrawForwardVerticalView(Transform origin, EnemyConfig config)
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
            forwardVerticalViewColor
        );
    }

    private void DrawTargetedVerticalView(EnemyConfig config)
    {
        Vector3 centerDirection = visionSensor.LastTargetedVerticalViewCenterDirection;
        Vector3 flatCenterDirection = Vector3.ProjectOnPlane(centerDirection, Vector3.up);

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
            visionSensor.LastTargetedVerticalViewOrigin,
            centerDirection,
            right,
            config.verticalViewAngle,
            visionSensor.LastTargetedVerticalViewDistance,
            targetedVerticalViewColor
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

    private bool TryGetConfig(out EnemyConfig config)
    {
        if (enemyController == null)
        {
            enemyController = GetComponent<NetworkEnemyController>();
        }

        config = enemyController != null ? enemyController.Config : null;
        return config != null;
    }

    private void CacheComponents()
    {
        if (visionSensor == null)
        {
            visionSensor = GetComponent<EnemyVisionSensor>();
        }

        if (enemyController == null)
        {
            enemyController = GetComponent<NetworkEnemyController>();
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        CacheComponents();
        originRadius = Mathf.Max(0.01f, originRadius);
    }
#endif
}