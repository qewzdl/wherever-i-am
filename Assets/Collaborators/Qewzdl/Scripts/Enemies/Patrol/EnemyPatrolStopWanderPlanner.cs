using UnityEngine;
using UnityEngine.AI;

public sealed class EnemyPatrolStopWanderPlanner
{
    private const float MinSampleRadius = 0.5f;
    private const float SampleRadiusMultiplier = 0.75f;

    public bool TryGetRandomWanderPoint(
        Vector3 center,
        Vector3 enemyPosition,
        float radius,
        float minDistanceFromEnemy,
        int attempts,
        out Vector3 point
    )
    {
        point = default;

        if (radius <= 0f || attempts <= 0)
        {
            return false;
        }

        float sqrMinDistanceFromEnemy = minDistanceFromEnemy * minDistanceFromEnemy;
        float sampleRadius = Mathf.Max(MinSampleRadius, radius * SampleRadiusMultiplier);

        for (int i = 0; i < attempts; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle * radius;

            Vector3 rawPoint = new Vector3(
                center.x + randomCircle.x,
                center.y,
                center.z + randomCircle.y
            );

            if (!NavMesh.SamplePosition(rawPoint, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
            {
                continue;
            }

            Vector3 sampledPoint = hit.position;
            Vector3 flatDeltaFromEnemy = sampledPoint - enemyPosition;
            flatDeltaFromEnemy.y = 0f;

            if (flatDeltaFromEnemy.sqrMagnitude < sqrMinDistanceFromEnemy)
            {
                continue;
            }

            point = sampledPoint;
            return true;
        }

        return false;
    }
}