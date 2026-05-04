using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(EnemyNavigator))]
[RequireComponent(typeof(EnemyAttackController))]
[RequireComponent(typeof(EnemyNavMeshStartupGate))]
public class NetworkEnemyController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyConfig config;
    [SerializeField] private EnemyPatrolRoute patrolRoute;
    [SerializeField] private EnemyTargetDetector targetDetector;
    [SerializeField] private EnemyNavigator navigator;
    [SerializeField] private EnemyAttackController attackController;
    [SerializeField] private EnemyNavMeshStartupGate navMeshStartupGate;

    private readonly NetworkVariable<EnemyState> currentState = new(
        EnemyState.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<ulong> currentTargetClientId = new(
        EnemyTargetMemory.NoTargetClientId,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private EnemyServerBrain brain;

    public EnemyConfig Config => config;
    public EnemyState CurrentState => currentState.Value;
    public ulong CurrentTargetClientId => currentTargetClientId.Value;
    public bool HasTarget => currentTargetClientId.Value != EnemyTargetMemory.NoTargetClientId;

    private void Awake()
    {
        CacheComponents();
    }

    public override void OnNetworkSpawn()
    {
        CacheComponents();

        if (!ValidateDependencies())
        {
            enabled = false;
            return;
        }

        if (!IsServer)
        {
            navigator.DisableAgent();
            return;
        }

        navigator.Configure(config);
        CreateBrainServer();
        TryStartBrainServer();
    }

    public override void OnNetworkDespawn()
    {
        if (navMeshStartupGate != null)
        {
            navMeshStartupGate.RemoveReadyListener(OnNavMeshReadyServer);
        }

        brain?.Dispose();
        brain = null;
    }

    private void Update()
    {
        if (!IsServer || brain == null)
        {
            return;
        }

        if (!brain.HasStarted)
        {
            TryStartBrainServer();
            return;
        }

        brain.Tick(Time.deltaTime);
    }

    private void CacheComponents()
    {
        if (targetDetector == null)
        {
            targetDetector = GetComponent<EnemyTargetDetector>();
        }

        if (navigator == null)
        {
            navigator = GetComponent<EnemyNavigator>();
        }

        if (attackController == null)
        {
            attackController = GetComponent<EnemyAttackController>();
        }

        if (navMeshStartupGate == null)
        {
            navMeshStartupGate = GetComponent<EnemyNavMeshStartupGate>();
        }
    }

    private bool ValidateDependencies()
    {
        if (config == null)
        {
            Debug.LogError($"{nameof(NetworkEnemyController)} requires {nameof(EnemyConfig)}.", this);
            return false;
        }

        if (navigator == null)
        {
            Debug.LogError($"{nameof(NetworkEnemyController)} requires {nameof(EnemyNavigator)}.", this);
            return false;
        }

        if (attackController == null)
        {
            Debug.LogError($"{nameof(NetworkEnemyController)} requires {nameof(EnemyAttackController)}.", this);
            return false;
        }

        if (navMeshStartupGate == null)
        {
            Debug.LogError($"{nameof(NetworkEnemyController)} requires {nameof(EnemyNavMeshStartupGate)}.", this);
            return false;
        }

        if (targetDetector == null)
        {
            Debug.LogWarning(
                $"{nameof(NetworkEnemyController)} has no {nameof(EnemyTargetDetector)}. Enemy will patrol but will not detect players.",
                this
            );
        }

        return true;
    }

    private void CreateBrainServer()
    {
        EnemyPatrolController patrolController = new EnemyPatrolController(
            patrolRoute,
            navigator,
            config
        );

        brain = new EnemyServerBrain(
            config,
            navigator,
            targetDetector,
            patrolController,
            attackController,
            SetStateServer,
            SetTargetClientIdServer
        );
    }

    private void TryStartBrainServer()
    {
        if (brain == null)
        {
            return;
        }

        if (navMeshStartupGate != null && !navMeshStartupGate.TryMakeReadyServer())
        {
            SetStateServer(EnemyState.Idle);
            navMeshStartupGate.AddReadyListener(OnNavMeshReadyServer);
            return;
        }

        brain.Start();
    }

    private void OnNavMeshReadyServer()
    {
        if (!IsServer)
        {
            return;
        }

        navMeshStartupGate.RemoveReadyListener(OnNavMeshReadyServer);
        TryStartBrainServer();
    }

    private void SetStateServer(EnemyState nextState)
    {
        if (!IsServer || currentState.Value == nextState)
        {
            return;
        }

        currentState.Value = nextState;
    }

    private void SetTargetClientIdServer(ulong targetClientId)
    {
        if (!IsServer || currentTargetClientId.Value == targetClientId)
        {
            return;
        }

        currentTargetClientId.Value = targetClientId;
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