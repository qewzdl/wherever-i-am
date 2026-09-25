using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
[RequireComponent(typeof(EnemyNetworkState))]
[RequireComponent(typeof(EnemyServerRuntime))]
[RequireComponent(typeof(EnemyNavigator))]
[RequireComponent(typeof(EnemyPhysicsMotor))]
[RequireComponent(typeof(EnemyItemPusher))]
public class NetworkEnemyController : NetworkBehaviour
{
    [Header("Config")]
    [SerializeField] private EnemyConfig config;

    [Tooltip(
        "This enemy's own config for each difficulty. When set, the difficulty " +
        "the host picked is looked up here, so every kind of enemy brings its " +
        "own tuning. Left empty, the enemy takes whatever config the lobby's " +
        "catalog resolved - which is only right while there is one kind of " +
        "enemy in the game.")]
    [SerializeField] private EnemyDifficultyCatalog difficultyCatalog;

    [SerializeField] private EnemyPatrolRoute patrolRoute;

    [Header("Runtime")]
    [SerializeField] private NetworkTransform networkTransform;
    [SerializeField] private EnemyNetworkState networkState;
    [SerializeField] private EnemyServerRuntime serverRuntime;
    [SerializeField] private MonoBehaviour clientPresentationBehaviour;

    [Header("Server Components")]
    [SerializeField] private EnemyTargetDetector targetDetector;
    [SerializeField] private EnemyNavigator navigator;
    [SerializeField] private EnemyAttackController attackController;
    [SerializeField] private EnemyNavMeshStartupGate navMeshStartupGate;
    [SerializeField] private EnemyPhysicsMotor physicsMotor;
    [SerializeField] private EnemyItemPusher itemPusher;

    private bool shouldStartServerRuntime;
    private IEnemyClientPresentation clientPresentation;

    public EnemyConfig Config => config;
    public EnemyDifficultyCatalog DifficultyCatalog => difficultyCatalog;
    public EnemyState CurrentState => networkState.CurrentState;
    public EnemyTargetIdentity CurrentTargetIdentity => networkState.CurrentTargetIdentity;
    public ulong CurrentTargetClientId => networkState.CurrentTargetClientId;
    public bool HasTarget => networkState.HasTarget;

    private void Awake()
    {
        CacheComponents();
    }

    // A spawned enemy comes from a prefab, and a prefab cannot hold a route
    // that lives in the scene. The spawner hands one over between Instantiate
    // and Spawn, while the runtime has not read it yet.
    public void ConstructServerOnly(EnemyPatrolRoute route)
    {
        if (IsSpawned)
        {
            Debug.LogError(
                $"{nameof(NetworkEnemyController)} cannot take a patrol route after spawning.",
                this);

            return;
        }

        patrolRoute = route;
    }

    public override void OnNetworkSpawn()
    {
        CacheComponents();
        ApplyLobbyDifficultyServerOnly();

        if (targetDetector != null &&
            NetworkObjectServiceContext.TryResolveSessionService(
                NetworkManager,
                out IGameplayNoiseService noiseService))
        {
            targetDetector.Construct(noiseService);
        }

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

    // The difficulty the host chose only ever changes how the enemy thinks,
    // which is server work. Clients keep the prefab config, and the catalog
    // makes sure every difficulty describes the same body, so the collider
    // they build from it stays right.
    //
    // Looked up in this enemy's own catalog by the difficulty's id, rather than
    // taken ready-made from the session, whenever the enemy has one. The session
    // resolves a single config from a single catalog, which was every enemy's
    // config while there was only one kind of enemy - and would quietly turn a
    // second kind into a copy of the first the moment it spawned, whatever it
    // had been tuned to be. The id is the part of the choice that is the same
    // for every enemy; what it means is each enemy's own business.
    private void ApplyLobbyDifficultyServerOnly()
    {
        if (!IsServer)
        {
            return;
        }

        if (!NetworkObjectServiceContext.TryResolveSessionService(
                NetworkManager,
                out IGameMapSessionService mapSession))
        {
            return;
        }

        config = ChooseDifficultyConfig(
            difficultyCatalog,
            mapSession.SelectedDifficultyId,
            mapSession.SelectedEnemyConfig,
            config);
    }

    // Which config an enemy plays a match with, given the difficulty the host
    // picked. Its own catalog's entry for that difficulty when it has one; the
    // config the lobby resolved when it does not; the prefab's own config when
    // there is neither.
    //
    // The order is the whole point. The lobby's config comes from the lobby's
    // catalog, which is one enemy's tuning - so preferring it over the enemy's
    // own would make every other kind of enemy play as that one, the moment it
    // spawned, whatever it had been set up to be.
    public static EnemyConfig ChooseDifficultyConfig(
        EnemyDifficultyCatalog ownCatalog,
        int selectedDifficultyId,
        EnemyConfig lobbyConfig,
        EnemyConfig prefabConfig)
    {
        if (ownCatalog != null &&
            ownCatalog.TryGetConfig(selectedDifficultyId, out EnemyConfig ownConfig))
        {
            return ownConfig;
        }

        return lobbyConfig != null ? lobbyConfig : prefabConfig;
    }

    public override void OnNetworkDespawn()
    {
        shouldStartServerRuntime = false;

        clientPresentation?.ShutdownPresentation();
        serverRuntime?.ShutdownServer();

        if (itemPusher != null)
        {
            itemPusher.enabled = false;
        }

        if (physicsMotor != null)
        {
            physicsMotor.enabled = false;
        }
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

        if (itemPusher != null)
        {
            itemPusher.enabled = true;
        }

        if (physicsMotor != null)
        {
            physicsMotor.enabled = true;
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

        if (itemPusher != null)
        {
            itemPusher.enabled = false;
        }

        if (physicsMotor != null)
        {
            physicsMotor.enabled = false;
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
        if (networkTransform == null)
        {
            networkTransform = GetComponent<NetworkTransform>();
        }

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
            clientPresentationBehaviour = ResolveClientPresentationBehaviour();
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

        if (itemPusher == null)
        {
            itemPusher = GetComponent<EnemyItemPusher>();
        }

        if (physicsMotor == null)
        {
            physicsMotor = GetComponent<EnemyPhysicsMotor>();
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

        if (networkTransform == null)
        {
            Debug.LogError(
                $"{nameof(NetworkEnemyController)} requires {nameof(NetworkTransform)} for enemy movement synchronization.",
                this
            );

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

        if (navigator == null)
        {
            Debug.LogError($"{nameof(NetworkEnemyController)} requires {nameof(EnemyNavigator)}.", this);
            return false;
        }

        if (itemPusher == null)
        {
            Debug.LogError(
                $"{nameof(NetworkEnemyController)} requires {nameof(EnemyItemPusher)}.",
                this
            );
            return false;
        }

        if (physicsMotor == null)
        {
            Debug.LogError(
                $"{nameof(NetworkEnemyController)} requires {nameof(EnemyPhysicsMotor)}.",
                this
            );
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

        if (itemPusher != null)
        {
            itemPusher.enabled = false;
        }

        if (physicsMotor != null)
        {
            physicsMotor.enabled = false;
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

        if (networkTransform == null)
        {
            Debug.LogError(
                $"{nameof(NetworkEnemyController)} requires {nameof(NetworkTransform)} for enemy movement synchronization.",
                this
            );
        }

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

    private MonoBehaviour ResolveClientPresentationBehaviour()
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
