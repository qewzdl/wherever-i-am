using UnityEngine;

public class EnemyPatrolRoute : MonoBehaviour
{
    [SerializeField] private Transform[] points;

    public int Count => points == null ? 0 : points.Length;

    public bool HasPoints => Count > 0;

    public Transform GetPoint(int index)
    {
        if (!HasPoints)
        {
            return null;
        }

        int safeIndex = Mathf.Abs(index) % points.Length;
        return points[safeIndex];
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (points == null || points.Length == 0)
        {
            return;
        }

        Gizmos.color = Color.yellow;

        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == null)
            {
                continue;
            }

            Gizmos.DrawSphere(points[i].position, 0.2f);

            Transform nextPoint = points[(i + 1) % points.Length];

            if (nextPoint != null)
            {
                Gizmos.DrawLine(points[i].position, nextPoint.position);
            }
        }
    }
#endif
}