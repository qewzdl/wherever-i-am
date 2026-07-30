using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public sealed class EnemyInvestigationSearchPlanner
{
    private const float MinAngleStep = 1f;
    private const float NavMeshSampleRadiusMultiplier = 0.65f;

    private readonly List<EnemyInvestigationSearchPoint> points = new();

    public IReadOnlyList<EnemyInvestigationSearchPoint> Points => points;
    public int PointCount => points.Count;

    public void BuildHierarchicalSearchPlan(
        Vector3 origin,
        Vector3 enemyPosition,
        float branchRadius,
        int branchPointCount,
        float leafRadius,
        int leafPointCountPerBranch)
    {
        NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(0);
        BuildHierarchicalSearchPlan(
            origin,
            enemyPosition,
            branchRadius,
            branchPointCount,
            leafRadius,
            leafPointCountPerBranch,
            new NavMeshQueryFilter
            {
                agentTypeID = settings.agentTypeID,
                areaMask = NavMesh.AllAreas
            });
    }

    public void BuildHierarchicalSearchPlan(
        Vector3 origin,
        Vector3 enemyPosition,
        float branchRadius,
        int branchPointCount,
        float leafRadius,
        int leafPointCountPerBranch,
        NavMeshQueryFilter filter
    )
    {
        points.Clear();

        if (branchPointCount <= 0 || branchRadius <= 0f)
        {
            return;
        }

        float branchAngleStep = Mathf.Max(MinAngleStep, 360f / branchPointCount);
        float branchStartAngle = GetFlatAngle(origin, enemyPosition) + 45f;
        int validBranchIndex = 0;

        for (int i = 0; i < branchPointCount; i++)
        {
            float angle = branchStartAngle + branchAngleStep * i;
            Vector3 branchDirection = GetDirectionFromAngle(angle);
            Vector3 rawBranchPoint = origin + branchDirection * branchRadius;

            if (!TrySampleNavMeshPoint(
                    rawBranchPoint,
                    branchRadius,
                    filter,
                    out Vector3 branchPoint))
            {
                continue;
            }

            int branchRouteIndex = points.Count;

            points.Add(new EnemyInvestigationSearchPoint(
                branchPoint,
                depth: 1,
                parentIndex: -1,
                branchIndex: validBranchIndex,
                localIndex: -1
            ));

            BuildLeafPoints(
                branchRouteIndex,
                branchPoint,
                origin,
                leafRadius,
                leafPointCountPerBranch,
                validBranchIndex,
                filter
            );

            validBranchIndex++;
        }
    }

    public bool TryGetPoint(int index, out Vector3 point)
    {
        if (index < 0 || index >= points.Count)
        {
            point = default;
            return false;
        }

        point = points[index].Position;
        return true;
    }

    private void BuildLeafPoints(
        int parentRouteIndex,
        Vector3 branchPoint,
        Vector3 origin,
        float leafRadius,
        int leafPointCount,
        int branchIndex,
        NavMeshQueryFilter filter
    )
    {
        if (leafPointCount <= 0 || leafRadius <= 0f)
        {
            return;
        }

        float leafAngleStep = Mathf.Max(MinAngleStep, 360f / leafPointCount);
        float leafStartAngle = GetFlatAngle(branchPoint, origin) + 60f;
        int validLocalIndex = 0;

        for (int i = 0; i < leafPointCount; i++)
        {
            float angle = leafStartAngle + leafAngleStep * i;
            Vector3 leafDirection = GetDirectionFromAngle(angle);
            Vector3 rawLeafPoint = branchPoint + leafDirection * leafRadius;

            if (!TrySampleNavMeshPoint(
                    rawLeafPoint,
                    leafRadius,
                    filter,
                    out Vector3 leafPoint))
            {
                continue;
            }

            points.Add(new EnemyInvestigationSearchPoint(
                leafPoint,
                depth: 2,
                parentIndex: parentRouteIndex,
                branchIndex: branchIndex,
                localIndex: validLocalIndex
            ));

            validLocalIndex++;
        }
    }

    private bool TrySampleNavMeshPoint(
        Vector3 rawPoint,
        float radius,
        NavMeshQueryFilter filter,
        out Vector3 sampledPoint)
    {
        float sampleRadius = Mathf.Max(0.5f, radius * NavMeshSampleRadiusMultiplier);

        if (NavMesh.SamplePosition(
                rawPoint,
                out NavMeshHit hit,
                sampleRadius,
                filter))
        {
            sampledPoint = hit.position;
            return true;
        }

        sampledPoint = default;
        return false;
    }

    private Vector3 GetDirectionFromAngle(float angle)
    {
        return new Vector3(
            Mathf.Cos(angle * Mathf.Deg2Rad),
            0f,
            Mathf.Sin(angle * Mathf.Deg2Rad)
        );
    }

    private float GetFlatAngle(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
        {
            return 0f;
        }

        return Mathf.Atan2(direction.z, direction.x) * Mathf.Rad2Deg;
    }
}
