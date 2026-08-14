using UnityEngine;
using UnityEngine.AI;

internal sealed class EnemyNavigationQueryService
{
    private readonly EnemyNavigationQueryTelemetry telemetry;
    private int remainingPathQueries;

    public EnemyNavigationQueryService(EnemyNavigationQueryTelemetry telemetry)
    {
        this.telemetry = telemetry;
    }

    public void BeginRepath(int maximumPathQueries)
    {
        remainingPathQueries = Mathf.Max(1, maximumPathQueries);
    }

    public bool TrySamplePosition(
        Vector3 source,
        float radius,
        NavMeshQueryFilter filter,
        out NavMeshHit hit)
    {
        telemetry.RecordPositionSample();
        return NavMesh.SamplePosition(source, out hit, radius, filter);
    }

    public bool TryCalculatePath(
        Vector3 source,
        Vector3 destination,
        NavMeshQueryFilter filter,
        NavMeshPath path)
    {
        if (path == null || remainingPathQueries <= 0)
        {
            telemetry.RecordBudgetRejection();
            return false;
        }

        remainingPathQueries--;
        telemetry.RecordPathQuery();
        path.ClearCorners();
        return NavMesh.CalculatePath(source, destination, filter, path);
    }

    public bool TryBuildPath(
        Vector3 source,
        Vector3 destination,
        float sourceSampleRadius,
        float destinationSampleRadius,
        NavMeshQueryFilter filter,
        NavMeshPath path,
        out Vector3 sampledDestination)
    {
        sampledDestination = destination;

        if (!TrySamplePosition(source, sourceSampleRadius, filter, out NavMeshHit sourceHit) ||
            !TrySamplePosition(destination, destinationSampleRadius, filter, out NavMeshHit destinationHit))
        {
            path?.ClearCorners();
            return false;
        }

        sampledDestination = destinationHit.position;
        return TryCalculatePath(
            sourceHit.position,
            destinationHit.position,
            filter,
            path);
    }

    public bool TryApplyPath(NavMeshAgent agent, NavMeshPath path, float speed)
    {
        if (agent == null ||
            path == null ||
            !agent.enabled ||
            !agent.isOnNavMesh ||
            path.status == NavMeshPathStatus.PathInvalid)
        {
            return false;
        }

        agent.speed = Mathf.Max(0f, speed);
        agent.isStopped = false;

        if (!agent.SetPath(path))
        {
            return false;
        }

        telemetry.RecordAppliedPath();
        return true;
    }
}
