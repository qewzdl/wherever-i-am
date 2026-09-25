using UnityEngine;

public class EnemyPatrolRoute : MonoBehaviour
{
    [SerializeField] private Transform[] points;

    public int Count => points == null ? 0 : points.Length;

    public bool HasPoints => Count > 0;

    // Which point sits closest to somewhere. Negative when the route has
    // nothing to offer - no points at all, or every one of them an empty slot
    // left in the array by a designer.
    //
    // Squared distances, because nothing here needs the real one and the
    // comparison is the same either way.
    public int NearestPointIndex(Vector3 position)
    {
        int nearest = -1;
        float nearestDistance = float.PositiveInfinity;

        for (int i = 0; i < Count; i++)
        {
            Transform point = points[i];

            if (point == null)
            {
                continue;
            }

            float distance = (point.position - position).sqrMagnitude;

            if (distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = distance;
            nearest = i;
        }

        return nearest;
    }

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

        // How far an enemy wanders at each stop is drawn by the spawn point
        // that sends one here: a route does not know which enemy walks it,
        // and holding one enemy's config to find out tied the map to her.
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == null)
            {
                continue;
            }

            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(points[i].position, 0.2f);

            Transform nextPoint = points[(i + 1) % points.Length];

            if (nextPoint != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(points[i].position, nextPoint.position);
            }
        }
    }
#endif
}