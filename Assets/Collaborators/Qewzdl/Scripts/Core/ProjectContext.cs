using System;
using Unity.Netcode;
using UnityEngine;

[DefaultExecutionOrder(-1100)]
[DisallowMultipleComponent]
public sealed class ProjectContext : MonoBehaviour, IDisposable
{
    private static ProjectContext instance;

    [Header("Project")]
    [SerializeField] private ProjectSceneRegistry sceneRegistry;
    [SerializeField] private ProjectSceneServiceComposer sceneServiceComposer;

    [Header("Network")]
    [SerializeField] private NetworkManager networkManager;

    [Header("Services")]
    [SerializeField] private GameStateMachine stateMachine;
    [SerializeField] private NetworkSessionOrchestrator sessionOrchestrator;
    [SerializeField] private NetworkConnectionService connectionService;
    [SerializeField] private NetworkConnectionApprovalService connectionApprovalService;
    [SerializeField] private UiErrorManager uiErrorManager;
    [SerializeField] private AudioManager audioManager;
    [SerializeField] private GameplayNoiseWorldService gameplayNoiseWorldService;
    [SerializeField] private GameMapService gameMapService;

    private bool referencesValidated;
    private bool referenceValidationFailureLogged;
    private bool ownsRuntimeContext;
    private ServiceScope globalServiceScope;
    private ServiceRegistrationTransaction globalScopeTransaction;
    private bool globalScopeCommitted;

    public static ProjectContext Instance => instance;
    public ProjectRuntimeLifecycleState LifecycleState { get; private set; }
    public bool IsReady => LifecycleState == ProjectRuntimeLifecycleState.Ready;
    public IServiceResolver Services => globalScopeCommitted &&
                                        globalServiceScope != null &&
                                        !globalServiceScope.IsDisposed
        ? globalServiceScope
        : null;
    public IServiceResolver SessionServices => sessionOrchestrator != null
        ? sessionOrchestrator.SessionServices
        : null;

    public ProjectSceneRegistry SceneRegistry => sceneRegistry;
    public ProjectSettings Settings => sceneRegistry != null ? sceneRegistry.Settings : null;
    public ProjectSceneFlow SceneFlow => sceneRegistry != null ? sceneRegistry.SceneFlow : null;
    public NetworkManager NetworkManager => networkManager;
    public INetworkSessionService SessionService => SessionOrchestrator;
    public GameStateMachine StateMachine => stateMachine;
    public NetworkSessionOrchestrator SessionOrchestrator => sessionOrchestrator;
    public NetworkConnectionService ConnectionService => connectionService;
    public NetworkConnectionApprovalService ConnectionApprovalService => connectionApprovalService;
    public LocalSceneLoader LocalSceneLoader => sceneServiceComposer != null
        ? sceneServiceComposer.LocalSceneLoader
        : null;
    public NetworkSceneLoader NetworkSceneLoader => sceneServiceComposer != null
        ? sceneServiceComposer.NetworkSceneLoader
        : null;
    public NetworkSceneLoader SceneLoader => NetworkSceneLoader;
    public ProjectSceneNavigator SceneNavigator => sceneServiceComposer != null
        ? sceneServiceComposer.SceneNavigator
        : null;
    public ProjectSceneFlowService SceneFlowService => sceneServiceComposer != null
        ? sceneServiceComposer.SceneFlowService
        : null;
    public UiErrorManager UiErrors => uiErrorManager;
    public AudioManager Audio => audioManager;
    public GameplayNoiseWorldService GameplayNoiseWorld => gameplayNoiseWorldService;
    public GameMapService GameMaps => gameMapService;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        ownsRuntimeContext = true;
    }

    private void OnDestroy()
    {
        if (!ownsRuntimeContext)
            return;

        AppRuntime runtime = AppRuntime.Instance;

        if (runtime != null)
            runtime.DisposeSceneScopes(this);

        ShutdownRuntime();
        DisposeRuntime();

        if (instance == this)
            instance = null;
    }

    public void MakePersistent()
    {
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    public bool StartRuntime()
    {
        if (IsReady)
            return true;

        if (IsLifecycleTransitionInProgress())
        {
            Debug.LogError(
                $"{nameof(ProjectContext)} cannot start while lifecycle state is {LifecycleState}.",
                this);

            return false;
        }

        try
        {
            LifecycleState = ProjectRuntimeLifecycleState.Validating;

            if (!ValidateReferences())
                return FailStartup("Validate");

            LifecycleState = ProjectRuntimeLifecycleState.Composing;

            if (!ComposeProjectServices())
                return FailStartup("Compose");

            LifecycleState = ProjectRuntimeLifecycleState.Initializing;

            if (!InitializeProjectServices())
                return FailStartup("Initialize");

            CommitGlobalServiceScope();

            LifecycleState = ProjectRuntimeLifecycleState.Ready;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            return FailStartup(LifecycleState.ToString());
        }
    }

    public void ShutdownRuntime()
    {
        if (LifecycleState == ProjectRuntimeLifecycleState.None ||
            LifecycleState == ProjectRuntimeLifecycleState.Disposed ||
            LifecycleState == ProjectRuntimeLifecycleState.ShuttingDown ||
            LifecycleState == ProjectRuntimeLifecycleState.Disposing)
        {
            return;
        }

        LifecycleState = ProjectRuntimeLifecycleState.ShuttingDown;
        ShutdownProjectServices();
    }

    public void Shutdown()
    {
        ShutdownRuntime();
    }

    public void DisposeRuntime()
    {
        if (LifecycleState == ProjectRuntimeLifecycleState.Disposed)
            return;

        if (LifecycleState != ProjectRuntimeLifecycleState.ShuttingDown)
            ShutdownRuntime();

        LifecycleState = ProjectRuntimeLifecycleState.Disposing;
        DisposeProjectServices();
        DisposeGlobalServiceScope();

        referencesValidated = false;
        referenceValidationFailureLogged = false;
        LifecycleState = ProjectRuntimeLifecycleState.Disposed;
    }

    public void Dispose()
    {
        DisposeRuntime();
    }

    public string GetSceneName(ProjectSceneKind sceneKind)
    {
        return sceneRegistry != null
            ? sceneRegistry.GetSceneName(sceneKind)
            : string.Empty;
    }

    public string GetScenePath(ProjectSceneKind sceneKind)
    {
        return sceneRegistry != null
            ? sceneRegistry.GetScenePath(sceneKind)
            : string.Empty;
    }

    public ProjectSceneKind GetActiveSceneKind()
    {
        return sceneRegistry != null
            ? sceneRegistry.GetActiveSceneKind()
            : ProjectSceneKind.Unknown;
    }

    public ProjectSceneKind GetSceneKind(string sceneName)
    {
        return sceneRegistry != null
            ? sceneRegistry.GetSceneKind(sceneName)
            : ProjectSceneKind.Unknown;
    }

    public ProjectSceneKind GetSceneKind(string sceneName, string scenePath)
    {
        return sceneRegistry != null
            ? sceneRegistry.GetSceneKind(sceneName, scenePath)
            : ProjectSceneKind.Unknown;
    }

    public bool IsScene(ProjectSceneKind sceneKind, string sceneName)
    {
        return sceneRegistry != null && sceneRegistry.IsScene(sceneKind, sceneName);
    }

    public ProjectSceneKind GetBootstrapSceneKind()
    {
        return sceneRegistry != null
            ? sceneRegistry.GetBootstrapSceneKind()
            : ProjectSceneKind.Unknown;
    }

    public ProjectSceneKind GetDefaultStartupScene()
    {
        return sceneRegistry != null
            ? sceneRegistry.GetDefaultStartupScene()
            : ProjectSceneKind.Unknown;
    }

    public GameState GetStateForScene(ProjectSceneKind sceneKind)
    {
        return sceneRegistry != null
            ? sceneRegistry.GetStateForScene(sceneKind)
            : GameState.Error;
    }

    public bool TryGetScene(ProjectSceneKind sceneKind, out ProjectSceneDefinition scene)
    {
        if (sceneRegistry != null)
            return sceneRegistry.TryGetScene(sceneKind, out scene);

        scene = default;
        return false;
    }

    private bool ValidateReferences()
    {
        if (referencesValidated)
            return true;

        bool logErrors = !referenceValidationFailureLogged;
        bool valid = true;

        valid &= ValidateRequiredReference(sceneRegistry, nameof(sceneRegistry), logErrors);
        valid &= ValidateRequiredReference(sceneServiceComposer, nameof(sceneServiceComposer), logErrors);
        valid &= ValidateRequiredReference(networkManager, nameof(networkManager), logErrors);
        valid &= ValidateRequiredReference(stateMachine, nameof(stateMachine), logErrors);
        valid &= ValidateRequiredReference(sessionOrchestrator, nameof(sessionOrchestrator), logErrors);
        valid &= ValidateRequiredReference(connectionService, nameof(connectionService), logErrors);
        valid &= ValidateRequiredReference(connectionApprovalService, nameof(connectionApprovalService), logErrors);
        valid &= ValidateRequiredReference(uiErrorManager, nameof(uiErrorManager), logErrors);
        valid &= ValidateRequiredReference(audioManager, nameof(audioManager), logErrors);
        valid &= ValidateRequiredReference(gameplayNoiseWorldService, nameof(gameplayNoiseWorldService), logErrors);
        valid &= ValidateRequiredReference(gameMapService, nameof(gameMapService), logErrors);

        if (valid)
            valid = sceneServiceComposer.Validate(this, logErrors);

        referencesValidated = valid;
        referenceValidationFailureLogged = !valid;

        return valid;
    }

    private bool ComposeProjectServices()
    {
        if (!BeginGlobalScopeComposition())
            return false;

        if (sceneServiceComposer == null || !sceneServiceComposer.Compose(this))
            return false;

        if (!RegisterGlobalServiceContracts())
            return false;

        return ConfigureSessionScopeController();
    }

    private bool InitializeProjectServices()
    {
        if (sceneServiceComposer == null || !sceneServiceComposer.Initialize())
            return false;

        return gameplayNoiseWorldService != null &&
               gameplayNoiseWorldService.Construct(networkManager);
    }

    private void ShutdownProjectServices()
    {
        try
        {
            gameplayNoiseWorldService?.Shutdown();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, gameplayNoiseWorldService);
        }

        try
        {
            sceneServiceComposer?.Shutdown();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, sceneServiceComposer);
        }
    }

    private void DisposeProjectServices()
    {
        try
        {
            sessionOrchestrator?.DisposeSessionScopeController();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, sessionOrchestrator);
        }

        try
        {
            gameplayNoiseWorldService?.DisposeRuntime();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, gameplayNoiseWorldService);
        }

        try
        {
            sceneServiceComposer?.Dispose();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, sceneServiceComposer);
        }
    }

    private bool BeginGlobalScopeComposition()
    {
        if (globalServiceScope != null || globalScopeTransaction != null)
        {
            Debug.LogError("Global service scope composition is already active.", this);
            return false;
        }

        globalScopeCommitted = false;
        globalServiceScope = new ServiceScope("Global");
        globalScopeTransaction = globalServiceScope.BeginRegistrationTransaction();
        return true;
    }

    private bool ConfigureSessionScopeController()
    {
        if (globalServiceScope == null || globalScopeTransaction == null)
        {
            Debug.LogError("Cannot configure Session scope before Global scope composition.", this);
            return false;
        }

        return sessionOrchestrator != null &&
               sessionOrchestrator.ConfigureSessionScopeController(
                   globalServiceScope,
                   gameMapService,
                   gameplayNoiseWorldService);
    }

    private bool RegisterGlobalServiceContracts()
    {
        if (globalServiceScope == null || globalScopeTransaction == null)
        {
            Debug.LogError("Global service scope composition has not started.", this);
            return false;
        }

        ProjectSceneFlowService projectSceneFlowService = SceneFlowService;
        GameMapCatalog mapCatalog = gameMapService != null
            ? gameMapService.Catalog
            : null;

        if (projectSceneFlowService == null)
        {
            Debug.LogError(
                $"Cannot register {nameof(IProjectSceneFlowService)} before scene services are composed.",
                this);

            return false;
        }

        if (mapCatalog == null)
        {
            Debug.LogError(
                $"Cannot register {nameof(IGameMapCatalog)} because {nameof(GameMapService)} has no catalog.",
                this);

            return false;
        }

        globalServiceScope.Register<IProjectSceneRegistry>(sceneRegistry);
        globalServiceScope.Register<IGameStateService>(stateMachine);
        globalServiceScope.Register<IProjectSceneFlowService>(projectSceneFlowService);
        globalServiceScope.Register<INetworkSessionService>(sessionOrchestrator);
        globalServiceScope.Register<INetworkConnectionService>(connectionService);
        globalServiceScope.Register<IUiErrorService>(uiErrorManager);
        globalServiceScope.Register<IAudioService>(audioManager);
        globalServiceScope.Register<IGameMapCatalog>(mapCatalog);
        return true;
    }

    private void CommitGlobalServiceScope()
    {
        if (globalServiceScope == null || globalScopeTransaction == null)
            throw new InvalidOperationException("Global service scope is not ready to commit.");

        globalScopeTransaction.Commit();
        globalScopeTransaction = null;
        globalScopeCommitted = true;
    }

    private void DisposeGlobalServiceScope()
    {
        globalScopeCommitted = false;

        ServiceRegistrationTransaction transaction = globalScopeTransaction;
        ServiceScope scope = globalServiceScope;
        globalScopeTransaction = null;
        globalServiceScope = null;

        if (transaction != null)
        {
            try
            {
                transaction.Rollback();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        if (scope == null)
            return;

        try
        {
            scope.Dispose();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
    }

    private bool FailStartup(string phase)
    {
        Debug.LogError($"{nameof(ProjectContext)} failed during {phase}. Rolling back bootstrap.", this);

        ShutdownProjectServices();
        DisposeProjectServices();
        DisposeGlobalServiceScope();

        referencesValidated = false;
        LifecycleState = ProjectRuntimeLifecycleState.Disposed;
        return false;
    }

    private bool IsLifecycleTransitionInProgress()
    {
        return LifecycleState == ProjectRuntimeLifecycleState.Validating ||
               LifecycleState == ProjectRuntimeLifecycleState.Composing ||
               LifecycleState == ProjectRuntimeLifecycleState.Initializing ||
               LifecycleState == ProjectRuntimeLifecycleState.ShuttingDown ||
               LifecycleState == ProjectRuntimeLifecycleState.Disposing;
    }

    private bool ValidateRequiredReference(UnityEngine.Object reference, string fieldName, bool logError)
    {
        if (reference != null)
            return true;

        if (logError)
            Debug.LogError($"{nameof(ProjectContext)} is missing '{fieldName}'.", this);

        return false;
    }
}
