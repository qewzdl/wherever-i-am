using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class EnemyInvestigationGizmos : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkEnemyController enemyController;
    [SerializeField] private EnemyServerRuntime serverRuntime;
    [SerializeField] private EnemyConfig previewConfig;

    [Header("Drawing")]
    [SerializeField] private bool drawOnlyWhenSelected = true;
    [SerializeField] private bool drawRuntimePlan = true;
    [SerializeField] private bool drawEditorPreview = true;
#if UNITY_EDITOR
    [SerializeField] private bool drawLabels = true;
#endif

    [Header("Preview")]
    [SerializeField] private float editorPreviewOriginDistance = 4f;

    [Header("Sizes")]
    [SerializeField] private float originRadius = 0.25f;
    [SerializeField] private float branchPointRadius = 0.18f;
    [SerializeField] private float leafPointRadius = 0.13f;
    [SerializeField] private float activePointRadius = 0.28f;
    [SerializeField] private float lineHeightOffset = 0.05f;

    private readonly EnemyInvestigationSearchPlanner previewPlanner = new();

    private void Awake()
    {
        RuntimeDebugBuildGuard.DestroyIfDisabled(this);
    }

    private void OnDrawGizmos()
    {
        if (drawOnlyWhenSelected)
        {
            return;
        }

        Draw();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawOnlyWhenSelected)
        {
            return;
        }

        Draw();
    }

    private void Draw()
    {
        CacheComponents();

        EnemyConfig config = ResolveConfig();

        if (config == null)
        {
            return;
        }

        if (drawRuntimePlan && Application.isPlaying && TryDrawRuntimePlan(config))
        {
            return;
        }

        if (!Application.isPlaying && drawEditorPreview)
        {
            DrawEditorPreview(config);
        }
    }

    private bool TryDrawRuntimePlan(EnemyConfig config)
    {
        if (serverRuntime == null || serverRuntime.InvestigationDebugData == null)
        {
            return false;
        }

        EnemyInvestigationDebugData debugData = serverRuntime.InvestigationDebugData;

        if (!debugData.HasOrigin)
        {
            return false;
        }

        DrawPlan(
            debugData.Origin,
            debugData.SearchPoints,
            debugData.ActiveRouteIndex,
            config,
            debugData.IsActive
        );

        if (debugData.HasCurrentDestination)
        {
            DrawCurrentDestination(debugData.CurrentDestination);
        }

        return true;
    }

    private void DrawEditorPreview(EnemyConfig config)
    {
        Vector3 previewOrigin = transform.position + transform.forward * editorPreviewOriginDistance;

        previewPlanner.BuildHierarchicalSearchPlan(
            previewOrigin,
            transform.position,
            config.investigationBranchRadius,
            config.investigationBranchPointCount,
            config.investigationLeafRadius,
            config.investigationLeafPointCountPerBranch
        );

        DrawPlan(
            previewOrigin,
            previewPlanner.Points,
            activeRouteIndex: -1,
            config,
            isActive: false
        );
    }

    private void DrawPlan(
        Vector3 origin,
        IReadOnlyList<EnemyInvestigationSearchPoint> points,
        int activeRouteIndex,
        EnemyConfig config,
        bool isActive
    )
    {
        if (points == null)
        {
            return;
        }

        Vector3 liftedOrigin = Lift(origin);

        Gizmos.color = isActive ? Color.red : new Color(1f, 0.25f, 0.25f, 0.65f);
        Gizmos.DrawSphere(liftedOrigin, originRadius);

        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.35f);
        Gizmos.DrawWireSphere(liftedOrigin, config.investigationBranchRadius);

        DrawLabel(liftedOrigin, "A");

        for (int i = 0; i < points.Count; i++)
        {
            EnemyInvestigationSearchPoint point = points[i];
            Vector3 liftedPoint = Lift(point.Position);
            Vector3 parentPosition = Lift(GetParentPosition(origin, points, point));

            DrawConnection(parentPosition, liftedPoint, point.Depth);
            DrawPoint(point, liftedPoint, i == activeRouteIndex);

            if (point.Depth == 1)
            {
                Gizmos.color = new Color(0.2f, 0.65f, 1f, 0.25f);
                Gizmos.DrawWireSphere(liftedPoint, config.investigationLeafRadius);
            }

            DrawPointLabel(point, liftedPoint);
        }
    }

    private void DrawConnection(Vector3 from, Vector3 to, int depth)
    {
        Gizmos.color = depth == 1
            ? new Color(0.2f, 0.65f, 1f, 0.85f)
            : new Color(0.25f, 1f, 0.45f, 0.85f);

        Gizmos.DrawLine(from, to);
    }

    private void DrawPoint(
        EnemyInvestigationSearchPoint point,
        Vector3 position,
        bool isActive
    )
    {
        if (isActive)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(position, activePointRadius);
            return;
        }

        if (point.Depth == 1)
        {
            Gizmos.color = new Color(0.2f, 0.65f, 1f, 0.95f);
            Gizmos.DrawSphere(position, branchPointRadius);
            return;
        }

        Gizmos.color = new Color(0.25f, 1f, 0.45f, 0.95f);
        Gizmos.DrawSphere(position, leafPointRadius);
    }

    private void DrawCurrentDestination(Vector3 destination)
    {
        Vector3 liftedDestination = Lift(destination);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(liftedDestination, activePointRadius * 1.5f);
    }

    private Vector3 GetParentPosition(
        Vector3 origin,
        IReadOnlyList<EnemyInvestigationSearchPoint> points,
        EnemyInvestigationSearchPoint point
    )
    {
        if (point.ParentIndex < 0)
        {
            return origin;
        }

        if (point.ParentIndex >= points.Count)
        {
            return origin;
        }

        return points[point.ParentIndex].Position;
    }

    private Vector3 Lift(Vector3 position)
    {
        position.y += lineHeightOffset;
        return position;
    }

    private EnemyConfig ResolveConfig()
    {
        if (previewConfig != null)
        {
            return previewConfig;
        }

        if (enemyController != null)
        {
            return enemyController.Config;
        }

        return null;
    }

    private void CacheComponents()
    {
        if (enemyController == null)
        {
            enemyController = GetComponent<NetworkEnemyController>();
        }

        if (serverRuntime == null)
        {
            serverRuntime = GetComponent<EnemyServerRuntime>();
        }
    }

    private void DrawLabel(Vector3 position, string label)
    {
#if UNITY_EDITOR
        if (!drawLabels)
        {
            return;
        }

        Handles.Label(position + Vector3.up * 0.25f, label);
#endif
    }

    private void DrawPointLabel(EnemyInvestigationSearchPoint point, Vector3 position)
    {
#if UNITY_EDITOR
        if (!drawLabels)
        {
            return;
        }

        string label = point.Depth == 1
            ? $"B{point.BranchIndex + 1}"
            : $"C{point.BranchIndex + 1}.{point.LocalIndex + 1}";

        Handles.Label(position + Vector3.up * 0.2f, label);
#endif
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        CacheComponents();

        editorPreviewOriginDistance = Mathf.Max(0f, editorPreviewOriginDistance);
        originRadius = Mathf.Max(0.01f, originRadius);
        branchPointRadius = Mathf.Max(0.01f, branchPointRadius);
        leafPointRadius = Mathf.Max(0.01f, leafPointRadius);
        activePointRadius = Mathf.Max(0.01f, activePointRadius);
        lineHeightOffset = Mathf.Max(0f, lineHeightOffset);
    }
#endif
}
