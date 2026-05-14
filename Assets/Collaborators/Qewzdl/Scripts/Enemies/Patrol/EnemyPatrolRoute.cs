using UnityEngine;

public class EnemyPatrolRoute : MonoBehaviour
{
    [SerializeField] private Transform[] points;

#if UNITY_EDITOR
    [Header("Gizmos")]
    [SerializeField] private EnemyConfig previewConfig;
    [SerializeField] private bool drawStopRadius = true;
    [SerializeField] private float fallbackStopRadius = 2f;
#endif

    public int Count => points == null ? 0 : points.Length;

    public bool HasPoints => Count > 0;

    public Transform GetPoint(int index)
    {
        if (!HasPoints)
        {
            return null;
        }

        int safeIndex = WrapIndex(index, points.Length);
        return points[safeIndex];
    }

    private int WrapIndex(int index, int length)
    {
        return ((index % length) + length) % length;
    }

#if UNITY_EDITOR
    [ContextMenu("Collect Child Points")]
    private void CollectChildPoints()
    {
        int childCount = transform.childCount;
        points = new Transform[childCount];

        for (int i = 0; i < childCount; i++)
        {
            points[i] = transform.GetChild(i);
        }

        UnityEditor.EditorUtility.SetDirty(this);
    }

    private void OnDrawGizmos()
    {
        if (points == null || points.Length == 0)
        {
            return;
        }

        float stopRadius = previewConfig != null
            ? previewConfig.patrolStopWanderRadius
            : fallbackStopRadius;

        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == null)
            {
                continue;
            }

            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(points[i].position, 0.2f);

            if (drawStopRadius && stopRadius > 0f)
            {
                Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.35f);
                Gizmos.DrawWireSphere(points[i].position, stopRadius);
            }

            Transform nextPoint = points[(i + 1) % points.Length];

            if (nextPoint != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(points[i].position, nextPoint.position);
            }
        }
    }

    private void OnValidate()
    {
        fallbackStopRadius = Mathf.Max(0f, fallbackStopRadius);
    }
#endif
}