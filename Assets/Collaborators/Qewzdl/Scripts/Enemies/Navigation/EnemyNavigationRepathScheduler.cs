using UnityEngine;
using UnityEngine.AI;

internal sealed class EnemyNavigationRepathScheduler
{
    private bool hasAttempt;
    private Vector3 attemptedDestination;
    private float nextAttemptTime;
    private int topologyRevision = -1;

    public bool ShouldRepath(
        Vector3 destination,
        NavMeshAgent agent,
        EnemyNavigationConfig config,
        int currentTopologyRevision,
        bool force)
    {
        if (force || !hasAttempt || topologyRevision != currentTopologyRevision)
        {
            return true;
        }

        if (Time.time < nextAttemptTime)
        {
            return false;
        }

        if (agent == null ||
            !agent.enabled ||
            !agent.isOnNavMesh ||
            !agent.hasPath ||
            agent.isPathStale ||
            agent.pathStatus == NavMeshPathStatus.PathInvalid)
        {
            return true;
        }

        float threshold = config != null
            ? Mathf.Max(0.01f, config.destinationRepathDistance)
            : 0.3f;
        Vector3 delta = destination - attemptedDestination;
        delta.y = 0f;
        return delta.sqrMagnitude >= threshold * threshold;
    }

    public void RecordAttempt(
        Vector3 destination,
        EnemyNavigationConfig config,
        int currentTopologyRevision)
    {
        hasAttempt = true;
        attemptedDestination = destination;
        topologyRevision = currentTopologyRevision;
        nextAttemptTime = Time.time + (config != null
            ? Mathf.Max(0.05f, config.repathInterval)
            : 0.2f);
    }

    public void Invalidate()
    {
        hasAttempt = false;
        nextAttemptTime = 0f;
    }
}
