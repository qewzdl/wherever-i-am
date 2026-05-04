using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyNavigator : MonoBehaviour
{
    private const float NavMeshSampleRadius = 2f;

    [SerializeField] private NavMeshAgent agent;

    private bool warnedAboutMissingNavMesh;

    public Vector3 Position => transform.position;

    private void Awake()
    {
        CacheAgent();
    }

    public void Configure(EnemyConfig config)
    {
        if (config == null)
        {
            return;
        }

        CacheAgent();

        if (agent == null)
        {
            return;
        }

        agent.speed = config.patrolSpeed;
        agent.acceleration = config.acceleration;
        agent.angularSpeed = config.angularSpeed;
        agent.stoppingDistance = config.stoppingDistance;
    }

    public void DisableAgent()
    {
        CacheAgent();

        if (agent != null)
        {
            agent.enabled = false;
        }
    }

    public bool TryMoveTo(Vector3 destination, float speed)
    {
        if (!TryEnsureOnNavMesh())
        {
            return false;
        }

        agent.isStopped = false;
        agent.speed = speed;

        return TrySetDestination(destination);
    }

    public void Stop()
    {
        if (!TryEnsureOnNavMesh())
        {
            return;
        }

        agent.isStopped = true;
    }

    public void ResetPath()
    {
        if (!TryEnsureOnNavMesh())
        {
            return;
        }

        agent.ResetPath();
    }

    public bool HasReached(float reachDistance)
    {
        if (!TryEnsureOnNavMesh())
        {
            return false;
        }

        return !agent.pathPending && agent.remainingDistance <= reachDistance;
    }

    public bool TryEnsureOnNavMesh()
    {
        CacheAgent();

        if (agent == null || !agent.enabled || !agent.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (agent.isOnNavMesh)
        {
            warnedAboutMissingNavMesh = false;
            return true;
        }

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, NavMeshSampleRadius, NavMesh.AllAreas)
            && agent.Warp(hit.position))
        {
            warnedAboutMissingNavMesh = false;
            return true;
        }

        if (!warnedAboutMissingNavMesh)
        {
            Debug.LogWarning(
                $"{nameof(EnemyNavigator)} is waiting for its {nameof(NavMeshAgent)} to be placed on a NavMesh.",
                this
            );

            warnedAboutMissingNavMesh = true;
        }

        return false;
    }

    private bool TrySetDestination(Vector3 destination)
    {
        if (!TryEnsureOnNavMesh())
        {
            return false;
        }

        if (NavMesh.SamplePosition(destination, out NavMeshHit hit, NavMeshSampleRadius, NavMesh.AllAreas))
        {
            return agent.SetDestination(hit.position);
        }

        return agent.SetDestination(destination);
    }

    private void CacheAgent()
    {
        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheAgent();
    }

    private void OnValidate()
    {
        CacheAgent();
    }
#endif
}