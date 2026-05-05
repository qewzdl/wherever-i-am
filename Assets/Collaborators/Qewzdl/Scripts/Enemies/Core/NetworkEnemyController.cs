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
            enabled = false;
            return;
        }

        if (!IsServer)
        {
            serverRuntime.DisableClientSimulation();
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
        if (!IsServer || serverRuntime == null)
        {
            return;
        }

        if (shouldStartServerRuntime)
        {
            shouldStartServerRuntime = false;

            if (!serverRuntime.TryInitializeServer(config, patrolRoute, networkState))
            {
                enabled = false;
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
    }

    private bool ValidateDependencies()
    {
        if (config == null)
        {
            Debug.LogError($"{nameof(NetworkEnemyController)} requires {nameof(EnemyConfig)}.", this);
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

        return true;
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