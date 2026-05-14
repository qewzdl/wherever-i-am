using UnityEngine;
using UnityEngine.AI;

public sealed class EnemyInvestigationSearchPlanner
{
    private const float MinAngleStep = 1f;
    private const float NavMeshSampleRadiusMultiplier = 0.65f;

    private readonly Vector3[] points;
    private int pointCount;

    public int PointCount => pointCount;

    public EnemyInvestigationSearchPlanner(int capacity)
    {
        points = new Vector3[Mathf.Max(0, capacity)];
    }

    public void BuildSearchPoints(
        Vector3 origin,
        Vector3 enemyPosition,
        float radius,
        int requestedPointCount
    )
    {
        pointCount = 0;

        if (points.Length == 0 || requestedPointCount <= 0 || radius <= 0f)
        {
            return;
        }

        int maxPoints = Mathf.Min(points.Length, requestedPointCount);
        float angleStep = Mathf.Max(MinAngleStep, 360f / maxPoints);
        float startAngle = GetAngleFromOriginToEnemy(origin, enemyPosition) + 45f;
        float sampleRadius = Mathf.Max(0.5f, radius * NavMeshSampleRadiusMultiplier);

        for (int i = 0; i < maxPoints; i++)
        {
            float angle = startAngle + angleStep * i;
            Vector3 direction = new Vector3(
                Mathf.Cos(angle * Mathf.Deg2Rad),
                0f,
                Mathf.Sin(angle * Mathf.Deg2Rad)
            );

            Vector3 rawPoint = origin + direction * radius;

            if (!NavMesh.SamplePosition(rawPoint, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
            {
                continue;
            }

            points[pointCount] = hit.position;
            pointCount++;
        }
    }

    public bool TryGetPoint(int index, out Vector3 point)
    {
        if (index < 0 || index >= pointCount)
        {
            point = default;
            return false;
        }

        point = points[index];
        return true;
    }

    private float GetAngleFromOriginToEnemy(Vector3 origin, Vector3 enemyPosition)
    {
        Vector3 direction = enemyPosition - origin;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
        {
            return 0f;
        }

        return Mathf.Atan2(direction.z, direction.x) * Mathf.Rad2Deg;
    }
}