using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NavMeshAgent))]
public class NetworkEnemyController : NetworkBehaviour
{
    private const ulong NoTargetClientId = ulong.MaxValue;
    private const float NavMeshSampleRadius = 2f;

    [Header("References")]
    [SerializeField] private EnemyConfig config;
    [SerializeField] private EnemyPatrolRoute patrolRoute;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private Transform eyes;

    [Header("Detection")]
    [SerializeField] private LayerMask playerMask;
    [SerializeField] private LayerMask obstructionMask;

    private readonly NetworkVariable<EnemyState> currentState = new(
        EnemyState.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<ulong> currentTargetClientId = new(
        NoTargetClientId,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Transform currentTarget;
    private int patrolPointIndex;
    private float targetRefreshTimer;
    private float attackCooldownTimer;
    private bool warnedAboutMissingNavMesh;
    private Vector3 lastKnownTargetPosition;
    private bool hasLastKnownTargetPosition;

    public EnemyState CurrentState => currentState.Value;
    public ulong CurrentTargetClientId => currentTargetClientId.Value;
    public bool HasTarget => currentTargetClientId.Value != NoTargetClientId;

    private void Awake()
    {
        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
        }

        if (eyes == null)
        {
            eyes = transform;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (config == null)
        {
            Debug.LogError($"{nameof(NetworkEnemyController)} requires {nameof(EnemyConfig)}.", this);
            enabled = false;
            return;
        }

        if (agent == null)
        {
            Debug.LogError($"{nameof(NetworkEnemyController)} requires {nameof(NavMeshAgent)}.", this);
            enabled = false;
            return;
        }

        if (IsServer)
        {
            ConfigureAgent();

            if (TryEnsureAgentOnNavMesh())
            {
                SetState(EnemyState.Patrol);
                MoveToNextPatrolPoint();
            }
            else
            {
                SetState(EnemyState.Idle);
            }
        }
        else
        {
            agent.enabled = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        currentTarget = null;
    }

    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        if (config == null || agent == null || !agent.enabled || !TryEnsureAgentOnNavMesh())
        {
            return;
        }

        TickServer(Time.deltaTime);
    }

    private void TickServer(float deltaTime)
    {
        attackCooldownTimer -= deltaTime;
        targetRefreshTimer -= deltaTime;

        if (targetRefreshTimer <= 0f)
        {
            targetRefreshTimer = config.targetRefreshInterval;
            RefreshTargetServer();
        }

        switch (currentState.Value)
        {
            case EnemyState.Idle:
                TickIdleServer();
                break;

            case EnemyState.Patrol:
                TickPatrolServer();
                break;

            case EnemyState.Chase:
                TickChaseServer();
                break;

            case EnemyState.Investigate:
                TickInvestigateServer();
                break;

            case EnemyState.Attack:
                TickAttackServer();
                break;
        }
    }

    private void TickIdleServer()
    {
        if (currentTarget != null)
        {
            StartChaseServer();
            return;
        }

        if (patrolRoute != null && patrolRoute.HasPoints)
        {
            SetState(EnemyState.Patrol);
            MoveToNextPatrolPoint();
        }
    }

    private void TickPatrolServer()
    {
        if (currentTarget != null)
        {
            StartChaseServer();
            return;
        }

        if (patrolRoute == null || !patrolRoute.HasPoints)
        {
            SetState(EnemyState.Idle);
            ResetAgentPath();
            return;
        }

        if (!TryEnsureAgentOnNavMesh())
        {
            return;
        }

        if (!agent.pathPending && agent.remainingDistance <= config.patrolPointReachDistance)
        {
            MoveToNextPatrolPoint();
        }
    }

    private void TickChaseServer()
    {
        if (currentTarget == null)
        {
            StopChaseServer();
            return;
        }

        float distanceToTarget = Vector3.Distance(transform.position, currentTarget.position);

        if (distanceToTarget > config.loseTargetDistance)
        {
            ClearTargetServer();
            StopChaseServer();
            return;
        }

        if (distanceToTarget <= config.attackDistance)
        {
            StartAttackServer();
            return;
        }

        if (!TryEnsureAgentOnNavMesh())
        {
            return;
        }

        agent.isStopped = false;
        agent.speed = config.chaseSpeed;

        if (TrySetDestination(currentTarget.position))
        {
            lastKnownTargetPosition = currentTarget.position;
            hasLastKnownTargetPosition = true;
        }
    }

    private void TickAttackServer()
    {
        if (currentTarget == null)
        {
            StopChaseServer();
            return;
        }

        float distanceToTarget = Vector3.Distance(transform.position, currentTarget.position);

        if (distanceToTarget > config.attackDistance)
        {
            StartChaseServer();
            return;
        }

        if (TryEnsureAgentOnNavMesh())
        {
            agent.isStopped = true;
        }

        if (attackCooldownTimer > 0f)
        {
            return;
        }

        attackCooldownTimer = config.attackCooldown;
        PerformAttackServer();
    }

    private void TickInvestigateServer()
    {
        if (currentTarget != null)
        {
            StartChaseServer();
            return;
        }

        if (!hasLastKnownTargetPosition)
        {
            StopChaseServer();
            return;
        }

        if (!TryEnsureAgentOnNavMesh())
        {
            return;
        }

        agent.isStopped = false;
        agent.speed = config.chaseSpeed;

        TrySetDestination(lastKnownTargetPosition);

        if (!agent.pathPending && agent.remainingDistance <= config.patrolPointReachDistance)
        {
            hasLastKnownTargetPosition = false;
            StopChaseServer();
        }
    }

    private void ConfigureAgent()
    {
        agent.speed = config.patrolSpeed;
        agent.acceleration = config.acceleration;
        agent.angularSpeed = config.angularSpeed;
        agent.stoppingDistance = config.stoppingDistance;
    }

    private void RefreshTargetServer()
    {
        Transform bestTarget = FindBestVisibleTargetServer();

        if (bestTarget == null)
        {
            if (currentState.Value == EnemyState.Chase || currentState.Value == EnemyState.Attack)
            {
                ClearTargetServer();

                if (hasLastKnownTargetPosition)
                {
                    StartInvestigateServer();
                }
                else
                {
                    StopChaseServer();
                }

                return;
            }

            ClearTargetServer();
            return;
        }

        currentTarget = bestTarget;

        NetworkObject targetNetworkObject = bestTarget.GetComponentInParent<NetworkObject>();

        if (targetNetworkObject != null && targetNetworkObject.IsSpawned)
        {
            currentTargetClientId.Value = targetNetworkObject.OwnerClientId;
        }
    }

    private Transform FindBestVisibleTargetServer()
    {
        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            config.detectionRadius,
            playerMask,
            QueryTriggerInteraction.Ignore
        );

        Transform bestTarget = null;
        float bestDistanceSqr = float.MaxValue;

        foreach (Collider hit in hits)
        {
            if (hit == null)
            {
                continue;
            }

            NetworkObject networkObject = hit.GetComponentInParent<NetworkObject>();

            if (networkObject == null || !networkObject.IsSpawned)
            {
                continue;
            }

            Transform candidate = networkObject.transform;
            Vector3 toCandidate = GetTargetPoint(candidate) - eyes.position;
            float distanceSqr = toCandidate.sqrMagnitude;

            if (distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            if (!CanSeeTarget(candidate))
            {
                continue;
            }

            bestTarget = candidate;
            bestDistanceSqr = distanceSqr;
        }

        return bestTarget;
    }

    private bool CanSeeTarget(Transform target)
    {
        if (target == null || eyes == null)
        {
            return false;
        }

        Vector3 targetPoint = GetTargetPoint(target);
        Vector3 directionToTarget = targetPoint - eyes.position;
        float distanceToTarget = directionToTarget.magnitude;

        if (distanceToTarget > config.detectionRadius)
        {
            return false;
        }

        float angle = Vector3.Angle(transform.forward, directionToTarget);

        if (angle > config.viewAngle * 0.5f)
        {
            return false;
        }

        if (obstructionMask.value == 0)
        {
            return true;
        }

        bool blocked = Physics.Raycast(
            eyes.position,
            directionToTarget.normalized,
            distanceToTarget,
            obstructionMask,
            QueryTriggerInteraction.Ignore
        );

        return !blocked;
    }

    private Vector3 GetTargetPoint(Transform target)
    {
        return target.position + Vector3.up * config.targetHeightOffset;
    }

    private void StartInvestigateServer()
    {
        if (!hasLastKnownTargetPosition)
        {
            StopChaseServer();
            return;
        }

        SetState(EnemyState.Investigate);

        if (TryEnsureAgentOnNavMesh())
        {
            agent.isStopped = false;
            agent.speed = config.chaseSpeed;
            TrySetDestination(lastKnownTargetPosition);
        }
    }

    private void StartChaseServer()
    {
        SetState(EnemyState.Chase);

        if (TryEnsureAgentOnNavMesh())
        {
            agent.isStopped = false;
            agent.speed = config.chaseSpeed;
        }
    }

    private void StopChaseServer()
    {
        if (patrolRoute != null && patrolRoute.HasPoints)
        {
            SetState(EnemyState.Patrol);
            MoveToNextPatrolPoint();
            return;
        }

        SetState(EnemyState.Idle);
        ResetAgentPath();
    }

    private void StartAttackServer()
    {
        SetState(EnemyState.Attack);
        ResetAgentPath();

        if (TryEnsureAgentOnNavMesh())
        {
            agent.isStopped = true;
        }
    }

    private void PerformAttackServer()
    {
        if (currentTarget == null)
        {
            return;
        }

        NetworkObject targetNetworkObject = currentTarget.GetComponentInParent<NetworkObject>();

        if (targetNetworkObject == null || !targetNetworkObject.IsSpawned)
        {
            ClearTargetServer();
            StopChaseServer();
            return;
        }

        Debug.Log($"Enemy attacked client {targetNetworkObject.OwnerClientId}.", this);

        // Future extension point:
        // 1. Get player health/caught component.
        // 2. Validate attack distance again.
        // 3. Apply server-side damage/caught state.
        // 4. Trigger ClientRpc for one-shot visual/audio feedback if needed.
    }

    private void MoveToNextPatrolPoint()
    {
        if (patrolRoute == null || !patrolRoute.HasPoints)
        {
            SetState(EnemyState.Idle);
            return;
        }

        if (!TryEnsureAgentOnNavMesh())
        {
            return;
        }

        Transform point = patrolRoute.GetPoint(patrolPointIndex);
        patrolPointIndex++;

        if (point == null)
        {
            return;
        }

        agent.isStopped = false;
        agent.speed = config.patrolSpeed;
        TrySetDestination(point.position);
    }

    private void ResetAgentPath()
    {
        if (TryEnsureAgentOnNavMesh())
        {
            agent.ResetPath();
        }
    }

    private bool TrySetDestination(Vector3 destination)
    {
        if (!TryEnsureAgentOnNavMesh())
        {
            return false;
        }

        if (NavMesh.SamplePosition(destination, out NavMeshHit hit, NavMeshSampleRadius, NavMesh.AllAreas))
        {
            return agent.SetDestination(hit.position);
        }

        return agent.SetDestination(destination);
    }

    private bool TryEnsureAgentOnNavMesh()
    {
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
                $"{nameof(NetworkEnemyController)} is waiting for its {nameof(NavMeshAgent)} to be placed on a NavMesh.",
                this
            );
            warnedAboutMissingNavMesh = true;
        }

        return false;
    }

    private void ClearTargetServer()
    {
        currentTarget = null;
        currentTargetClientId.Value = NoTargetClientId;
    }

    private void ClearTargetMemoryServer()
    {
        ClearTargetServer();
        hasLastKnownTargetPosition = false;
    }

    private void SetState(EnemyState nextState)
    {
        if (currentState.Value == nextState)
        {
            return;
        }

        currentState.Value = nextState;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (config == null)
        {
            return;
        }

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, config.detectionRadius);

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, config.attackDistance);
    }
#endif
}
