using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyNavigator))]
[RequireComponent(typeof(EnemyAttackController))]
[RequireComponent(typeof(EnemyNavMeshStartupGate))]
public class EnemyServerRuntime : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyTargetDetector targetDetector;
    [SerializeField] private EnemyNavigator navigator;
    [SerializeField] private EnemyAttackController attackController;
    [SerializeField] private EnemyNavMeshStartupGate navMeshStartupGate;
    [SerializeField] private EnemyPostureController postureController;

    private readonly EnemyBlackboard blackboard = new();

    private EnemyConfig config;
    private EnemyPatrolRoute patrolRoute;
    private EnemyNetworkState networkState;
    private EnemyServerBrain brain;

    private bool initializedServer;
    private bool subscribedToAttackPhase;

    public bool IsRunning => brain != null && brain.HasStarted;
    public EnemyInvestigationDebugData InvestigationDebugData => blackboard.InvestigationDebugData;

    private void Awake()
    {
        CacheComponents();
    }

    public bool TryInitializeServer(
        EnemyConfig enemyConfig,
        EnemyPatrolRoute enemyPatrolRoute,
        EnemyNetworkState enemyNetworkState
    )
    {
        ShutdownServer();
        CacheComponents();

        config = enemyConfig;
        patrolRoute = enemyPatrolRoute;
        networkState = enemyNetworkState;

        if (!ValidateDependencies())
        {
            initializedServer = false;
            return false;
        }

        navigator.Configure(config);

        if (networkState.IsSpawned)
        {
            networkState.ClearAttackPhaseServer();
        }

        SubscribeToAttackPhaseServer();
        CreateBrainServer();

        initializedServer = true;
        TryStartBrainServer();

        return true;
    }

    public void TickServer(float deltaTime)
    {
        if (!initializedServer || brain == null)
        {
            return;
        }

        if (!brain.HasStarted)
        {
            TryStartBrainServer();
            return;
        }

        brain.Tick(deltaTime);
    }

    public void ShutdownServer()
    {
        if (navMeshStartupGate != null)
        {
            navMeshStartupGate.RemoveReadyListener(OnNavMeshReadyServer);
        }

        brain?.Dispose();
        brain = null;

        UnsubscribeFromAttackPhaseServer();

        if (networkState != null && networkState.IsSpawned)
        {
            networkState.ClearAttackPhaseServer();
        }

        blackboard.ClearAll();

        initializedServer = false;
        config = null;
        patrolRoute = null;
        networkState = null;
    }

    public void DisableClientSimulation(EnemyConfig enemyConfig)
    {
        CacheComponents();

        config = enemyConfig;

        if (postureController != null)
        {
            postureController.Configure(config);
        }

        navigator?.DisableAgent();
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

        if (postureController == null)
        {
            postureController = GetComponent<EnemyPostureController>();
        }
    }

    private bool ValidateDependencies()
    {
        if (config == null)
        {
            Debug.LogError($"{nameof(EnemyServerRuntime)} requires {nameof(EnemyConfig)}.", this);
            return false;
        }

        if (networkState == null)
        {
            Debug.LogError($"{nameof(EnemyServerRuntime)} requires {nameof(EnemyNetworkState)}.", this);
            return false;
        }

        if (navigator == null)
        {
            Debug.LogError($"{nameof(EnemyServerRuntime)} requires {nameof(EnemyNavigator)}.", this);
            return false;
        }

        if (attackController == null)
        {
            Debug.LogError($"{nameof(EnemyServerRuntime)} requires {nameof(EnemyAttackController)}.", this);
            return false;
        }

        if (navMeshStartupGate == null)
        {
            Debug.LogError($"{nameof(EnemyServerRuntime)} requires {nameof(EnemyNavMeshStartupGate)}.", this);
            return false;
        }

        if (config.crawlingEnabled && postureController == null)
        {
            Debug.LogError(
                $"{nameof(EnemyServerRuntime)} requires {nameof(EnemyPostureController)} when crawling is enabled.",
                this
            );

            return false;
        }

        if (config.RequiresTargetDetector && targetDetector == null)
        {
            Debug.LogError(
                $"{nameof(EnemyServerRuntime)} requires {nameof(EnemyTargetDetector)} " +
                $"because {nameof(EnemyConfig)} '{config.name}' uses {config.BehaviorMode} behavior.",
                this
            );

            return false;
        }

        return true;
    }

    private void CreateBrainServer()
    {
        EnemyPatrolController patrolController = new EnemyPatrolController(
            patrolRoute,
            navigator,
            config,
            blackboard
        );

        bool usesTargetDetection = config.RequiresTargetDetector;

        brain = new EnemyServerBrain(
            config,
            navigator,
            usesTargetDetection ? targetDetector : null,
            usesTargetDetection,
            patrolController,
            attackController,
            postureController,
            blackboard,
            networkState.SetStateServer,
            networkState.SetTargetIdentityServer
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
            networkState.SetStateServer(EnemyState.Idle);
            navMeshStartupGate.AddReadyListener(OnNavMeshReadyServer);
            return;
        }

        brain.Start();
    }

    private void SubscribeToAttackPhaseServer()
    {
        if (subscribedToAttackPhase || attackController == null)
        {
            return;
        }

        attackController.PhaseChanged += HandleAttackPhaseChangedServer;
        subscribedToAttackPhase = true;
    }

    private void UnsubscribeFromAttackPhaseServer()
    {
        if (!subscribedToAttackPhase || attackController == null)
        {
            subscribedToAttackPhase = false;
            return;
        }

        attackController.PhaseChanged -= HandleAttackPhaseChangedServer;
        subscribedToAttackPhase = false;
    }

    private void HandleAttackPhaseChangedServer(EnemyAttackPhaseEvent phaseEvent)
    {
        if (!initializedServer || networkState == null || !networkState.IsSpawned)
        {
            return;
        }

        networkState.SetAttackPhaseServer(phaseEvent);
    }

    private void OnNavMeshReadyServer()
    {
        if (!initializedServer)
        {
            return;
        }

        navMeshStartupGate.RemoveReadyListener(OnNavMeshReadyServer);
        TryStartBrainServer();
    }

    private void OnDisable()
    {
        ShutdownServer();
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