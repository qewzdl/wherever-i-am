using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyServerRuntime))]
public class EnemyInvestigationGizmos : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyServerRuntime serverRuntime;

    [Header("Drawing")]
    [SerializeField] private bool drawOnlyWhenSelected = true;
    [SerializeField] private bool drawInactiveOrigin;
    [SerializeField] private float originRadius = 0.18f;
    [SerializeField] private float routePointRadius = 0.12f;
    [SerializeField] private float activePointRadius = 0.28f;
    [SerializeField] private float destinationRadius = 0.2f;
    [SerializeField] private float lineHeightOffset = 0.05f;

    [Header("Colors")]
    [SerializeField] private Color originColor = Color.cyan;
    [SerializeField] private Color branchPointColor = new(1f, 0.75f, 0f, 1f);
    [SerializeField] private Color leafPointColor = new(1f, 0.45f, 0f, 1f);
    [SerializeField] private Color activePointColor = Color.green;
    [SerializeField] private Color destinationColor = Color.magenta;
    [SerializeField] private Color routeLineColor = new(1f, 0.7f, 0f, 0.8f);

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

        if (serverRuntime == null)
        {
            return;
        }

        EnemyInvestigationDebugData data = serverRuntime.InvestigationDebugData;

        if (data == null)
        {
            return;
        }

        if (!data.IsActive && !drawInactiveOrigin)
        {
            return;
        }

        DrawOrigin(data);
        DrawCurrentDestination(data);
        DrawSearchRoute(data);
    }

    private void DrawOrigin(EnemyInvestigationDebugData data)
    {
        if (!data.HasOrigin)
        {
            return;
        }

        Gizmos.color = originColor;
        Gizmos.DrawSphere(data.Origin, Mathf.Max(0.01f, originRadius));
    }

    private void DrawCurrentDestination(EnemyInvestigationDebugData data)
    {
        if (!data.HasCurrentDestination)
        {
            return;
        }

        Gizmos.color = destinationColor;
        Gizmos.DrawSphere(data.CurrentDestination, Mathf.Max(0.01f, destinationRadius));
        Gizmos.DrawLine(
            transform.position + Vector3.up * lineHeightOffset,
            data.CurrentDestination + Vector3.up * lineHeightOffset
        );
    }

    private void DrawSearchRoute(EnemyInvestigationDebugData data)
    {
        IReadOnlyList<EnemyInvestigationSearchPoint> points = data.SearchPoints;

        if (points == null || points.Count == 0)
        {
            return;
        }

        for (int i = 0; i < points.Count; i++)
        {
            EnemyInvestigationSearchPoint point = points[i];

            DrawRoutePoint(point, i == data.ActiveRouteIndex);
            DrawRouteParentLine(points, point);
        }
    }

    private void DrawRoutePoint(EnemyInvestigationSearchPoint point, bool isActive)
    {
        if (isActive)
        {
            Gizmos.color = activePointColor;
            Gizmos.DrawSphere(point.Position, Mathf.Max(0.01f, activePointRadius));
            return;
        }

        Gizmos.color = point.Depth <= 1 ? branchPointColor : leafPointColor;
        Gizmos.DrawSphere(point.Position, Mathf.Max(0.01f, routePointRadius));
    }

    private void DrawRouteParentLine(
        IReadOnlyList<EnemyInvestigationSearchPoint> points,
        EnemyInvestigationSearchPoint point
    )
    {
        if (point.ParentIndex < 0 || point.ParentIndex >= points.Count)
        {
            return;
        }

        Vector3 from = points[point.ParentIndex].Position + Vector3.up * lineHeightOffset;
        Vector3 to = point.Position + Vector3.up * lineHeightOffset;

        Gizmos.color = routeLineColor;
        Gizmos.DrawLine(from, to);
    }

    private void CacheComponents()
    {
        if (serverRuntime == null)
        {
            serverRuntime = GetComponent<EnemyServerRuntime>();
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
        routePointRadius = Mathf.Max(0.01f, routePointRadius);
        activePointRadius = Mathf.Max(0.01f, activePointRadius);
        destinationRadius = Mathf.Max(0.01f, destinationRadius);
        lineHeightOffset = Mathf.Max(0f, lineHeightOffset);
    }
#endif
}