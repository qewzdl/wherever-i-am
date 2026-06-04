using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyPostureController))]
public class EnemyPostureGizmos : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkEnemyController enemyController;
    [SerializeField] private EnemyPostureController postureController;

    [Header("Drawing")]
    [SerializeField] private bool drawOnlyWhenSelected = true;
    [SerializeField] private bool drawStandingCapsule = true;
    [SerializeField] private bool drawCrawlingCapsule = true;
    [SerializeField] private bool highlightCurrentPosture = true;

    [Header("Colors")]
    [SerializeField] private Color standingColor = new(0.2f, 0.8f, 1f, 0.9f);
    [SerializeField] private Color crawlingColor = new(0.8f, 0.5f, 1f, 0.9f);
    [SerializeField] private Color currentPostureColor = Color.green;

    private void Awake()
    {
        if (RuntimeDebugBuildGuard.DestroyIfDisabled(this))
        {
            return;
        }

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

        if (!TryGetConfig(out EnemyConfig config))
        {
            return;
        }

        if (drawStandingCapsule)
        {
            Color color = ShouldHighlight(EnemyPosture.Standing)
                ? currentPostureColor
                : standingColor;

            DrawCapsule(
                config.standingBodyColliderCenter,
                config.standingBodyColliderHeight,
                config.standingBodyColliderRadius,
                color
            );
        }

        if (drawCrawlingCapsule)
        {
            Color color = ShouldHighlight(EnemyPosture.Crawling)
                ? currentPostureColor
                : crawlingColor;

            DrawCapsule(
                config.crawlingBodyColliderCenter,
                config.crawlingBodyColliderHeight,
                config.crawlingBodyColliderRadius,
                color
            );
        }
    }

    private bool ShouldHighlight(EnemyPosture posture)
    {
        return highlightCurrentPosture &&
               postureController != null &&
               postureController.CurrentPosture == posture;
    }

    private void DrawCapsule(
        Vector3 localCenter,
        float height,
        float radius,
        Color color
    )
    {
        Vector3 worldCenter = transform.position + transform.rotation * Vector3.Scale(
            localCenter,
            transform.lossyScale
        );

        Vector3 axis = transform.up;
        Vector3 right = transform.right;
        Vector3 forward = transform.forward;

        float heightScale = Mathf.Abs(transform.lossyScale.y);
        float radiusScale = Mathf.Max(
            Mathf.Abs(transform.lossyScale.x),
            Mathf.Abs(transform.lossyScale.z)
        );

        float worldRadius = Mathf.Max(0.01f, radius * radiusScale);
        float worldHeight = Mathf.Max(worldRadius * 2f, height * heightScale);
        float halfSegmentLength = Mathf.Max(0f, worldHeight * 0.5f - worldRadius);

        Vector3 top = worldCenter + axis * halfSegmentLength;
        Vector3 bottom = worldCenter - axis * halfSegmentLength;

        Gizmos.color = color;

        Gizmos.DrawWireSphere(top, worldRadius);
        Gizmos.DrawWireSphere(bottom, worldRadius);

        Gizmos.DrawLine(top + right * worldRadius, bottom + right * worldRadius);
        Gizmos.DrawLine(top - right * worldRadius, bottom - right * worldRadius);
        Gizmos.DrawLine(top + forward * worldRadius, bottom + forward * worldRadius);
        Gizmos.DrawLine(top - forward * worldRadius, bottom - forward * worldRadius);
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
        if (enemyController == null)
        {
            enemyController = GetComponent<NetworkEnemyController>();
        }

        if (postureController == null)
        {
            postureController = GetComponent<EnemyPostureController>();
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
    }
#endif
}
