using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyNavigator : MonoBehaviour
{
    private const float NavMeshSampleRadius = 2f;

    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private EnemyPostureController postureController;

    private NavMeshPath pathBuffer;

    private EnemyConfig config;
    private bool warnedAboutMissingNavMesh;

    public Vector3 Position => transform.position;

    private void Awake()
    {
        CacheComponents();
        pathBuffer = new NavMeshPath();
    }

    public void Configure(EnemyConfig config)
    {
        this.config = config;

        CacheComponents();

        if (config == null)
        {
            return;
        }

        postureController?.Configure(config);

        if (agent == null)
        {
            return;
        }

        agent.speed = config.patrolSpeed;
        agent.acceleration = config.acceleration;
        agent.angularSpeed = config.angularSpeed;
        agent.stoppingDistance = config.stoppingDistance;

        if (postureController != null)
        {
            postureController.TrySetServerPosture(EnemyPosture.Standing);
        }
    }

    public void DisableAgent()
    {
        CacheComponents();

        if (agent != null)
        {
            agent.enabled = false;
        }
    }

    public bool TryMoveTo(Vector3 destination, float speed)
    {
        if (config != null && config.crawlingEnabled && postureController != null)
        {
            if (TryMoveToWithPosture(destination, speed, EnemyPosture.Standing))
            {
                return true;
            }

            if (TryMoveToWithPosture(destination, speed, EnemyPosture.Crawling))
            {
                return true;
            }

            return false;
        }

        return TryMoveToWithCurrentPosture(destination, speed);
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
        CacheComponents();

        if (agent == null || !agent.enabled || !agent.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (agent.isOnNavMesh)
        {
            warnedAboutMissingNavMesh = false;
            return true;
        }

        if (TrySamplePositionForCurrentAgent(transform.position, out NavMeshHit hit)
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

    private bool TryMoveToWithPosture(
        Vector3 destination,
        float baseSpeed,
        EnemyPosture posture
    )
    {
        if (postureController == null)
        {
            return false;
        }

        if (!TryBuildCompletePathForPosture(destination, posture))
        {
            return false;
        }

        if (!postureController.TrySetServerPosture(posture))
        {
            return false;
        }

        float postureSpeed = postureController.GetSpeedForPosture(baseSpeed, posture);
        return TryMoveToWithCurrentPosture(destination, postureSpeed);
    }

    private bool TryMoveToWithCurrentPosture(Vector3 destination, float speed)
    {
        if (!TryEnsureOnNavMesh())
        {
            return false;
        }

        if (!TryBuildCompletePath(destination, out Vector3 sampledDestination))
        {
            return false;
        }

        agent.isStopped = false;
        agent.speed = speed;

        return agent.SetDestination(sampledDestination);
    }

    private bool TryBuildCompletePath(Vector3 destination, out Vector3 sampledDestination)
    {
        sampledDestination = destination;

        if (agent == null)
        {
            return false;
        }

        pathBuffer ??= new NavMeshPath();

        if (!TrySamplePositionForCurrentAgent(destination, out NavMeshHit hit))
        {
            return false;
        }

        sampledDestination = hit.position;

        if (!agent.CalculatePath(sampledDestination, pathBuffer))
        {
            return false;
        }

        return pathBuffer.status == NavMeshPathStatus.PathComplete;
    }

    private bool TrySamplePositionForCurrentAgent(Vector3 sourcePosition, out NavMeshHit hit)
    {
        if (agent == null)
        {
            hit = default;
            return false;
        }

        NavMeshQueryFilter filter = new()
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };

        return NavMesh.SamplePosition(sourcePosition, out hit, NavMeshSampleRadius, filter);
    }

    private bool TryBuildCompletePathForPosture(Vector3 destination, EnemyPosture posture)
    {
        if (agent == null || postureController == null)
        {
            return false;
        }
        
        if (!postureController.CanUsePostureAtCurrentPosition(posture))
        {
            return false;
        }

        pathBuffer ??= new NavMeshPath();

        NavMeshQueryFilter filter = new()
        {
            agentTypeID = postureController.GetAgentTypeIdForPosture(posture),
            areaMask = agent.areaMask
        };

        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit sourceHit, NavMeshSampleRadius, filter))
        {
            return false;
        }

        if (!NavMesh.SamplePosition(destination, out NavMeshHit destinationHit, NavMeshSampleRadius, filter))
        {
            return false;
        }

        if (!NavMesh.CalculatePath(sourceHit.position, destinationHit.position, filter, pathBuffer))
        {
            return false;
        }

        return pathBuffer.status == NavMeshPathStatus.PathComplete;
    }

    private void CacheComponents()
    {
        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
        }

        if (postureController == null)
        {
            postureController = GetComponent<EnemyPostureController>();
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
    }
#endif
}
