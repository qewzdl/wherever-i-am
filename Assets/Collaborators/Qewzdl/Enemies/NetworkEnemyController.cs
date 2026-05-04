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

    [Header("Navigation")]
    [SerializeField] private RuntimeNavMeshBuilder navMeshBuilder;
    [SerializeField] private bool waitForRuntimeNavMesh = true;

    [Header("Detection")]
    [SerializeField] private EnemyTargetDetector targetDetector;

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

    private EnemyTarget currentTarget;
    private int patrolPointIndex;
    private float targetRefreshTimer;
    private float attackCooldownTimer;
    private bool warnedAboutMissingNavMesh;
    private Vector3 lastKnownTargetPosition;
    private bool hasLastKnownTargetPosition;

    private bool aiStarted;
    private bool subscribedToNavMeshBuilder;

    public EnemyConfig Config => config;
    public EnemyState CurrentState => currentState.Value;
    public ulong CurrentTargetClientId => currentTargetClientId.Value;
    public bool HasTarget => currentTargetClientId.Value != NoTargetClientId;

    private void Awake()
    {
        if (targetDetector == null)
        {
            targetDetector = GetComponent<EnemyTargetDetector>();
        }

        if (targetDetector == null)
        {
            Debug.LogWarning(
                $"{nameof(NetworkEnemyController)} has no {nameof(EnemyTargetDetector)}. Enemy will patrol but will not detect players.",
                this
            );
        }

        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
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
            TryStartAiWhenReadyServer();
        }
        else
        {
            agent.enabled = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeFromNavMeshBuilderServer();

        currentTarget = null;
        aiStarted = false;
    }

    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        if (!aiStarted)
        {
            TryStartAiWhenReadyServer();
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
        if (!IsCurrentTargetValid())
        {
            ClearTargetServer();
            StopChaseServer();
            return;
        }

        Vector3 targetPosition = GetTargetNavigationPosition(currentTarget);
        float distanceToTarget = Vector3.Distance(transform.position, targetPosition);

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
        TrySetDestination(targetPosition);
    }

    private void TickAttackServer()
    {
        if (!IsCurrentTargetValid())
        {
            ClearTargetServer();
            StopChaseServer();
            return;
        }

        Vector3 targetPosition = GetTargetNavigationPosition(currentTarget);
        float distanceToTarget = Vector3.Distance(transform.position, targetPosition);

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

        if (!agent.pathPending && agent.remainingDistance <= config.patrolPointReachDistance)
        {
            ClearTargetMemoryServer();
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

    private void TryStartAiWhenReadyServer()
    {
        if (aiStarted)
        {
            return;
        }

        if (waitForRuntimeNavMesh && navMeshBuilder != null && !navMeshBuilder.HasBuilt)
        {
            if (navMeshBuilder.BuildIfAllowed())
            {
                StartAiServer();
                return;
            }

            SubscribeToNavMeshBuilderServer();
            SetState(EnemyState.Idle);
            return;
        }

        StartAiServer();
    }

    private void StartAiServer()
    {
        if (aiStarted)
        {
            return;
        }

        if (!TryEnsureAgentOnNavMesh())
        {
            SetState(EnemyState.Idle);
            return;
        }

        aiStarted = true;

        if (patrolRoute != null && patrolRoute.HasPoints)
        {
            SetState(EnemyState.Patrol);
            MoveToNextPatrolPoint();
        }
        else
        {
            SetState(EnemyState.Idle);
        }
    }

    private void SubscribeToNavMeshBuilderServer()
    {
        if (subscribedToNavMeshBuilder || navMeshBuilder == null)
        {
            return;
        }

        subscribedToNavMeshBuilder = true;
        navMeshBuilder.AddBuiltListener(OnRuntimeNavMeshBuiltServer);
    }

    private void UnsubscribeFromNavMeshBuilderServer()
    {
        if (!subscribedToNavMeshBuilder || navMeshBuilder == null)
        {
            return;
        }

        navMeshBuilder.RemoveBuiltListener(OnRuntimeNavMeshBuiltServer);
        subscribedToNavMeshBuilder = false;
    }

    private void OnRuntimeNavMeshBuiltServer(RuntimeNavMeshBuilder builder)
    {
        if (!IsServer)
        {
            return;
        }

        UnsubscribeFromNavMeshBuilderServer();
        TryStartAiWhenReadyServer();
    }

    private void RefreshTargetServer()
    {
        EnemyTarget bestTarget = targetDetector != null
            ? targetDetector.FindBestVisibleTarget(config)
            : null;

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

            if (currentState.Value != EnemyState.Investigate)
            {
                ClearTargetServer();
            }

            return;
        }

        currentTarget = bestTarget;
        RememberTargetPositionServer(GetTargetNavigationPosition(bestTarget));

        NetworkObject targetNetworkObject = bestTarget.NetworkObject;

        if (targetNetworkObject != null && targetNetworkObject.IsSpawned)
        {
            currentTargetClientId.Value = targetNetworkObject.OwnerClientId;
        }
        else
        {
            currentTargetClientId.Value = NoTargetClientId;
        }
    }

    private void StartInvestigateServer()
    {
        if (!hasLastKnownTargetPosition)
        {
            ClearTargetMemoryServer();
            StopChaseServer();
            return;
        }

        SetState(EnemyState.Investigate);

        if (!TryEnsureAgentOnNavMesh())
        {
            return;
        }

        agent.isStopped = false;
        agent.speed = config.chaseSpeed;

        if (!TrySetDestination(lastKnownTargetPosition))
        {
            ClearTargetMemoryServer();
            StopChaseServer();
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
        if (!IsCurrentTargetValid())
        {
            ClearTargetServer();
            StopChaseServer();
            return;
        }

        NetworkObject targetNetworkObject = currentTarget.NetworkObject;

        if (targetNetworkObject == null || !targetNetworkObject.IsSpawned)
        {
            ClearTargetServer();
            StopChaseServer();
            return;
        }

        Vector3 targetPosition = GetTargetNavigationPosition(currentTarget);
        float distanceToTarget = Vector3.Distance(transform.position, targetPosition);

        if (distanceToTarget > config.attackDistance)
        {
            StartChaseServer();
            return;
        }

        Debug.Log($"Enemy attacked client {targetNetworkObject.OwnerClientId}.", this);

        // Future extension point:
        // 1. Delegate to EnemyAttackHandler.
        // 2. Apply server-side caught/damage state.
        // 3. Trigger ClientRpc for one-shot feedback.
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

    private void RememberTargetPositionServer(Vector3 position)
    {
        lastKnownTargetPosition = position;
        hasLastKnownTargetPosition = true;
    }

    private bool IsCurrentTargetValid()
    {
        return currentTarget != null && currentTarget.IsValidNetworkTarget;
    }

    private Vector3 GetTargetNavigationPosition(EnemyTarget target)
    {
        if (target == null)
        {
            return transform.position;
        }

        NetworkObject targetNetworkObject = target.NetworkObject;

        if (targetNetworkObject != null && targetNetworkObject.IsSpawned)
        {
            return targetNetworkObject.transform.position;
        }

        return target.transform.position;
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

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, config.attackDistance);
    }
#endif
}
