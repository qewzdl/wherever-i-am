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
    [SerializeField] private MonoBehaviour clientPresentationBehaviour;

    [Header("Server Components")]
    [SerializeField] private EnemyTargetDetector targetDetector;
    [SerializeField] private EnemyNavigator navigator;
    [SerializeField] private EnemyAttackController attackController;
    [SerializeField] private EnemyNavMeshStartupGate navMeshStartupGate;

    private bool shouldStartServerRuntime;
    private IEnemyClientPresentation clientPresentation;

    public EnemyConfig Config => config;
    public EnemyState CurrentState => networkState.CurrentState;
    public EnemyTargetIdentity CurrentTargetIdentity => networkState.CurrentTargetIdentity;
    public ulong CurrentTargetClientId => networkState.CurrentTargetClientId;
    public bool HasTarget => networkState.HasTarget;

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

        if (IsClient)
        {
            bool disableLocalSimulation = !IsServer;

            if (!clientPresentation.InitializePresentation(
                    config,
                    networkState,
                    disableLocalSimulation
                ))
            {
                DisableRuntimeAfterInvalidConfiguration();
                return;
            }
        }
        else
        {
            DisableClientPresentation();
        }

        if (!IsServer)
        {
            DisableServerRuntimeOnClient();
            return;
        }

        EnableServerRuntimeComponents();
        shouldStartServerRuntime = true;
    }

    public override void OnNetworkDespawn()
    {
        shouldStartServerRuntime = false;

        clientPresentation?.ShutdownPresentation();
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

    private void EnableServerRuntimeComponents()
    {
        serverRuntime.enabled = true;

        if (targetDetector != null)
        {
            targetDetector.enabled = config.RequiresTargetDetector;
        }

        if (navigator != null)
        {
            navigator.enabled = true;
        }

        if (attackController != null)
        {
            attackController.enabled = true;
        }

        if (navMeshStartupGate != null)
        {
            navMeshStartupGate.enabled = true;
        }
    }

    private void DisableServerRuntimeOnClient()
    {
        shouldStartServerRuntime = false;

        if (serverRuntime != null)
        {
            serverRuntime.ShutdownServer();
            serverRuntime.enabled = false;
        }

        if (targetDetector != null)
        {
            targetDetector.enabled = false;
        }

        if (navigator != null)
        {
            navigator.enabled = false;
        }

        if (attackController != null)
        {
            attackController.enabled = false;
        }

        if (navMeshStartupGate != null)
        {
            navMeshStartupGate.enabled = false;
        }
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

        clientPresentation = clientPresentationBehaviour as IEnemyClientPresentation;

        if (clientPresentation == null)
        {
            clientPresentationBehaviour = FindClientPresentationBehaviour();
            clientPresentation = clientPresentationBehaviour as IEnemyClientPresentation;
        }

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

        if (clientPresentation == null)
        {
            Debug.LogError(
                $"{nameof(NetworkEnemyController)} requires a component that implements " +
                $"{nameof(IEnemyClientPresentation)}.",
                this
            );
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

        clientPresentation?.ShutdownPresentation();
        SetClientPresentationEnabled(false);

        if (serverRuntime != null)
        {
            serverRuntime.ShutdownServer();
            serverRuntime.enabled = false;
        }

        if (targetDetector != null)
        {
            targetDetector.enabled = false;
        }

        if (navigator != null)
        {
            navigator.enabled = false;
        }

        if (attackController != null)
        {
            attackController.enabled = false;
        }

        if (navMeshStartupGate != null)
        {
            navMeshStartupGate.enabled = false;
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

        if (clientPresentation == null)
        {
            Debug.LogError(
                $"{nameof(NetworkEnemyController)} requires a component that implements " +
                $"{nameof(IEnemyClientPresentation)}.",
                this
            );
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

    private MonoBehaviour FindClientPresentationBehaviour()
    {
        MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();

        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];

            if (behaviour is IEnemyClientPresentation)
            {
                return behaviour;
            }
        }

        return null;
    }

    private void DisableClientPresentation()
    {
        clientPresentation?.ShutdownPresentation();
        SetClientPresentationEnabled(false);
    }

    private void SetClientPresentationEnabled(bool isEnabled)
    {
        if (clientPresentationBehaviour != null)
        {
            clientPresentationBehaviour.enabled = isEnabled;
        }
    }
}
