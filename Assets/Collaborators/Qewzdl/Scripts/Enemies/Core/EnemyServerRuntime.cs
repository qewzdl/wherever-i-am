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

    private readonly EnemyInvestigationDebugData investigationDebugData = new();

    private EnemyConfig config;
    private EnemyPatrolRoute patrolRoute;
    private EnemyNetworkState networkState;
    private EnemyServerBrain brain;

    private bool initializedServer;

    public bool IsRunning => brain != null && brain.HasStarted;
    public EnemyInvestigationDebugData InvestigationDebugData => investigationDebugData;

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

        investigationDebugData.Clear();

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

        if (config != null && config.crawlingEnabled && postureController == null)
        {
            Debug.LogError(
                $"{nameof(EnemyServerRuntime)} requires {nameof(EnemyPostureController)} when crawling is enabled.",
                this
            );

            return false;
        }

        if (targetDetector == null)
        {
            Debug.LogWarning(
                $"{nameof(EnemyServerRuntime)} has no {nameof(EnemyTargetDetector)}. Enemy will patrol but will not detect players.",
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
            investigationDebugData,
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