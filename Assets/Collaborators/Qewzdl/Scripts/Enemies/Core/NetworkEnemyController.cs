using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(EnemyNetworkState))]
[RequireComponent(typeof(EnemyServerRuntime))]
public class NetworkEnemyController : NetworkBehaviour
{
    [Header("Config")]
    [SerializeField] private EnemyConfig config;
    [SerializeField] private EnemyPatrolRoute patrolRoute;

    [Header("Runtime")]
    [SerializeField] private EnemyNetworkState networkState;
    [SerializeField] private EnemyServerRuntime serverRuntime;
    [SerializeField] private EnemyTargetDetector targetDetector;

    private bool shouldStartServerRuntime;

    public EnemyConfig Config => config;

    public EnemyState CurrentState =>
        networkState != null ? networkState.CurrentState : EnemyState.Idle;

    public EnemyTargetIdentity CurrentTargetIdentity =>
        networkState != null ? networkState.CurrentTargetIdentity : EnemyTargetIdentity.None;

    public ulong CurrentTargetClientId =>
        networkState != null ? networkState.CurrentTargetClientId : EnemyTargetMemory.NoTargetClientId;

    public bool HasTarget => networkState != null && networkState.HasTarget;

    private void Awake()
    {
        CacheComponents();
    }

    public override void OnNetworkSpawn()
    {
        CacheComponents();

        if (!ValidateDependencies())
        {
            DisableRuntimeAfterInvalidConfiguration();
            return;
        }

        serverRuntime.enabled = true;

        if (!IsServer)
        {
            serverRuntime.DisableClientSimulation(config);
            return;
        }

        shouldStartServerRuntime = true;
    }

    public override void OnNetworkDespawn()
    {
        shouldStartServerRuntime = false;
        serverRuntime?.ShutdownServer();
    }

    private void Update()
    {
        if (!IsServer || serverRuntime == null || !serverRuntime.enabled)
        {
            return;
        }

        if (shouldStartServerRuntime)
        {
            shouldStartServerRuntime = false;

            if (!serverRuntime.TryInitializeServer(config, patrolRoute, networkState))
            {
                DisableRuntimeAfterInvalidConfiguration();
            }

            return;
        }

        serverRuntime.TickServer(Time.deltaTime);
    }

    private void CacheComponents()
    {
        if (networkState == null)
        {
            networkState = GetComponent<EnemyNetworkState>();
        }

        if (serverRuntime == null)
        {
            serverRuntime = GetComponent<EnemyServerRuntime>();
        }

        if (targetDetector == null)
        {
            targetDetector = GetComponent<EnemyTargetDetector>();
        }
    }

    private bool ValidateDependencies()
    {
        if (config == null)
        {
            Debug.LogError($"{nameof(NetworkEnemyController)} requires {nameof(EnemyConfig)}.", this);
            return false;
        }

        if (config.TryGetValidationError(out string configError))
        {
            Debug.LogError(configError, this);
            return false;
        }

        if (networkState == null)
        {
            Debug.LogError($"{nameof(NetworkEnemyController)} requires {nameof(EnemyNetworkState)}.", this);
            return false;
        }

        if (serverRuntime == null)
        {
            Debug.LogError($"{nameof(NetworkEnemyController)} requires {nameof(EnemyServerRuntime)}.", this);
            return false;
        }

        if (config.RequiresTargetDetector && targetDetector == null)
        {
            Debug.LogError(
                $"{nameof(NetworkEnemyController)} requires {nameof(EnemyTargetDetector)} " +
                $"because {nameof(EnemyConfig)} '{config.name}' uses {config.BehaviorMode} behavior.",
                this
            );

            return false;
        }

        return true;
    }

    private void DisableRuntimeAfterInvalidConfiguration()
    {
        shouldStartServerRuntime = false;

        if (serverRuntime != null)
        {
            serverRuntime.ShutdownServer();
            serverRuntime.enabled = false;
        }

        enabled = false;
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        CacheComponents();

        if (config == null)
        {
            return;
        }

        if (config.RequiresTargetDetector && targetDetector == null)
        {
            Debug.LogError(
                $"{nameof(NetworkEnemyController)} requires {nameof(EnemyTargetDetector)} " +
                $"because {nameof(EnemyConfig)} '{config.name}' uses {config.BehaviorMode} behavior.",
                this
            );
        }
    }
#endif
}