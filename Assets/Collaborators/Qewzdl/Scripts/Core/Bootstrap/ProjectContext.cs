using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-1100)]
[DisallowMultipleComponent]
public sealed class ProjectContext : MonoBehaviour
{
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
    [SerializeField] private SettingsService settingsService;
    [SerializeField] private AudioManager audioManager;
    [SerializeField] private GameplayNoiseWorldService gameplayNoiseWorldService;
    [SerializeField] private GameMapService gameMapService;

    private bool referencesValidated;
    private bool referenceValidationFailureLogged;
    private ServiceScope globalServiceScope;
    private ServiceRegistrationTransaction globalScopeTransaction;
    private bool globalScopeCommitted;
    private GlobalServicePublication globalServicesPublication;
    private SceneRuntimeScopeRegistry sceneRuntimeScopes;

    internal ProjectRuntimeLifecycleState LifecycleState { get; private set; }
    internal bool IsReady => LifecycleState == ProjectRuntimeLifecycleState.Ready;
    internal IServiceResolver Services => globalScopeCommitted &&
                                          globalServiceScope != null &&
                                          !globalServiceScope.IsDisposed
        ? globalServiceScope
        : null;
    internal ProjectSceneRegistry SceneRegistry => sceneRegistry;
    internal ProjectSettings Settings => sceneRegistry != null ? sceneRegistry.Settings : null;
    internal ProjectSceneFlow SceneFlow => sceneRegistry != null ? sceneRegistry.SceneFlow : null;
    internal NetworkManager NetworkManager => networkManager;
    internal INetworkSessionService SessionService => SessionOrchestrator;
    internal GameStateMachine StateMachine => stateMachine;
    internal NetworkSessionOrchestrator SessionOrchestrator => sessionOrchestrator;
    internal NetworkConnectionService ConnectionService => connectionService;
    internal NetworkConnectionApprovalService ConnectionApprovalService => connectionApprovalService;
    internal LocalSceneLoader LocalSceneLoader => sceneServiceComposer != null
        ? sceneServiceComposer.LocalSceneLoader
        : null;
    internal NetworkSceneLoader NetworkSceneLoader => sceneServiceComposer != null
        ? sceneServiceComposer.NetworkSceneLoader
        : null;
    internal NetworkSceneLoader SceneLoader => NetworkSceneLoader;
    internal ProjectSceneNavigator SceneNavigator => sceneServiceComposer != null
        ? sceneServiceComposer.SceneNavigator
        : null;
    internal ProjectSceneFlowService SceneFlowService => sceneServiceComposer != null
        ? sceneServiceComposer.SceneFlowService
        : null;
    private void OnDestroy()
    {
        ForceAbortRuntimeForApplicationQuit();
    }

    internal void MakePersistent()
    {
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    internal bool StartRuntime()
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
            PublishGlobalServices();

            LifecycleState = ProjectRuntimeLifecycleState.Ready;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            return FailStartup(LifecycleState.ToString());
        }
    }

    internal void ShutdownRuntime()
    {
        EnsureNoActiveNetworkSessionForSynchronousTeardown();
        ShutdownRuntimeCore();
    }

    private void ShutdownRuntimeCore()
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

    internal void DisposeRuntime()
    {
        EnsureNoActiveNetworkSessionForSynchronousTeardown();
        DisposeRuntimeCore();
    }

    private void DisposeRuntimeCore()
    {
        if (LifecycleState == ProjectRuntimeLifecycleState.Disposed)
            return;

        if (LifecycleState != ProjectRuntimeLifecycleState.ShuttingDown)
            ShutdownRuntimeCore();

        LifecycleState = ProjectRuntimeLifecycleState.Disposing;
        DisposeProjectServices();
        DisposeGlobalServiceScope();

        referencesValidated = false;
        referenceValidationFailureLogged = false;
        LifecycleState = ProjectRuntimeLifecycleState.Disposed;
    }

    internal void ForceAbortRuntimeForApplicationQuit()
    {
        if (LifecycleState == ProjectRuntimeLifecycleState.Disposed)
            return;

        sessionOrchestrator?.ForceAbortForApplicationQuit();
        DisposeSceneRuntimeScopes();
        ShutdownRuntimeCore();
        DisposeRuntimeCore();
    }

    private void EnsureNoActiveNetworkSessionForSynchronousTeardown()
    {
        if (sessionOrchestrator == null ||
            !sessionOrchestrator.RequiresCoordinatedShutdown)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{nameof(ProjectContext)} cannot synchronously tear down an active network " +
            $"session. Await {nameof(INetworkSessionService)}." +
            $"{nameof(INetworkSessionService.ShutdownToMainMenuAsync)} first.");
    }

    internal string GetSceneName(ProjectSceneKind sceneKind)
    {
        return sceneRegistry != null
            ? sceneRegistry.GetSceneName(sceneKind)
            : string.Empty;
    }

    internal string GetScenePath(ProjectSceneKind sceneKind)
    {
        return sceneRegistry != null
            ? sceneRegistry.GetScenePath(sceneKind)
            : string.Empty;
    }

    internal ProjectSceneKind GetActiveSceneKind()
    {
        return sceneRegistry != null
            ? sceneRegistry.GetActiveSceneKind()
            : ProjectSceneKind.Unknown;
    }

    internal ProjectSceneKind GetSceneKind(string sceneName)
    {
        return sceneRegistry != null
            ? sceneRegistry.GetSceneKind(sceneName)
            : ProjectSceneKind.Unknown;
    }

    internal ProjectSceneKind GetSceneKind(string sceneName, string scenePath)
    {
        return sceneRegistry != null
            ? sceneRegistry.GetSceneKind(sceneName, scenePath)
            : ProjectSceneKind.Unknown;
    }

    internal bool IsScene(ProjectSceneKind sceneKind, string sceneName)
    {
        return sceneRegistry != null && sceneRegistry.IsScene(sceneKind, sceneName);
    }

    internal ProjectSceneKind GetBootstrapSceneKind()
    {
        return sceneRegistry != null
            ? sceneRegistry.GetBootstrapSceneKind()
            : ProjectSceneKind.Unknown;
    }

    internal ProjectSceneKind GetDefaultStartupScene()
    {
        return sceneRegistry != null
            ? sceneRegistry.GetDefaultStartupScene()
            : ProjectSceneKind.Unknown;
    }

    internal GameState GetStateForScene(ProjectSceneKind sceneKind)
    {
        return sceneRegistry != null
            ? sceneRegistry.GetStateForScene(sceneKind)
            : GameState.Error;
    }

    internal bool TryGetScene(ProjectSceneKind sceneKind, out ProjectSceneDefinition scene)
    {
        if (sceneRegistry != null)
            return sceneRegistry.TryGetScene(sceneKind, out scene);

        scene = default;
        return false;
    }

    internal bool ConfigureSceneRuntimeScopes(SceneRuntimeScopeRegistry scopes)
    {
        if (scopes == null)
        {
            Debug.LogError($"{nameof(ProjectContext)} cannot use a null scene scope registry.", this);
            return false;
        }

        if (sceneRuntimeScopes == null || ReferenceEquals(sceneRuntimeScopes, scopes))
        {
            sceneRuntimeScopes = scopes;
            return true;
        }

        Debug.LogError($"{nameof(ProjectContext)} is already configured with another scene scope registry.", this);
        return false;
    }

    internal bool TryCreateSceneServiceScope(
        Scene scene,
        ProjectSceneKind sceneKind,
        out ServiceScope sceneScope,
        out SceneServiceScopeParent scopeParent)
    {
        sceneScope = null;
        scopeParent = default;

        if (!scene.IsValid() || !scene.isLoaded || !IsReady)
        {
            Debug.LogError("Cannot create a service scope for an invalid or unloaded scene.", this);
            return false;
        }

        bool isMapScene = IsGameMapScene(scene);

        if (!ProjectSceneScopePolicy.TryGetRequirements(
                sceneKind,
                isMapScene,
                out ProjectSceneScopeRequirements requirements))
        {
            Debug.LogError(
                $"Scene '{GetSceneLabel(scene)}' has no service scope policy. " +
                $"Configured kind: {sceneKind}.",
                this);

            return false;
        }

        scopeParent = requirements.Parent;
        ServiceScope parentScope;

        if (scopeParent == SceneServiceScopeParent.Global)
        {
            parentScope = globalServiceScope;
        }
        else
        {
            if (sessionOrchestrator == null ||
                !sessionOrchestrator.TryGetSessionServiceScope(out parentScope))
            {
                Debug.LogError(
                    $"Cannot create Session-owned scene scope for '{GetSceneLabel(scene)}' " +
                    "because no network Session scope is open.",
                    this);

                return false;
            }
        }

        if (parentScope == null || parentScope.IsDisposed)
        {
            Debug.LogError(
                $"Cannot create scene scope '{GetSceneLabel(scene)}' from an inactive {scopeParent} scope.",
                this);

            return false;
        }

        try
        {
            sceneScope = parentScope.CreateChild(
                $"Scene[{scene.handle}] {GetSceneLabel(scene)}",
                requirements.ServicePolicy);

            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to create service scope for scene '{GetSceneLabel(scene)}'.", this);
            Debug.LogException(exception, this);
            return false;
        }
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
        valid &= ValidateRequiredReference(settingsService, nameof(settingsService), logErrors);
        valid &= ValidateRequiredReference(audioManager, nameof(audioManager), logErrors);
        valid &= ValidateRequiredReference(gameplayNoiseWorldService, nameof(gameplayNoiseWorldService), logErrors);
        valid &= ValidateRequiredReference(gameMapService, nameof(gameMapService), logErrors);

        if (sceneRuntimeScopes == null)
        {
            if (logErrors)
            {
                Debug.LogError(
                    $"{nameof(ProjectContext)} is missing {nameof(SceneRuntimeScopeRegistry)} configuration.",
                    this);
            }

            valid = false;
        }

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
        if (settingsService == null || !settingsService.Initialize())
            return false;

        if (audioManager == null || !audioManager.Construct(sceneRegistry, settingsService))
            return false;

        uiErrorManager.Construct(audioManager);

        if (gameMapService == null || !gameMapService.Construct(sceneRegistry, networkManager))
            return false;

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
            uiErrorManager?.DisposeComposition();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, uiErrorManager);
        }

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
        globalServiceScope = new ServiceScope(
            "Global",
            GlobalServiceContractPolicy.Instance);
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
                   gameplayNoiseWorldService,
                   sceneRuntimeScopes);
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
        globalServiceScope.Register<INetworkSessionAdmissionService>(
            connectionApprovalService);
        globalServiceScope.Register<IUiErrorService>(uiErrorManager);
        globalServiceScope.Register<ISettingsService>(settingsService);
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

    private void PublishGlobalServices()
    {
        if (!globalScopeCommitted || Services == null)
        {
            throw new InvalidOperationException(
                "Global services cannot be published before scope commit.");
        }

        if (globalServicesPublication != null)
        {
            throw new InvalidOperationException(
                "Global services are already published by this ProjectContext.");
        }

        globalServicesPublication = G.Publish(Services, GetGlobalPublicationOwner());
    }

    private string GetGlobalPublicationOwner()
    {
        string objectName = gameObject != null
            ? gameObject.name
            : name;

        return $"{nameof(ProjectContext)} '{objectName}' (instanceId={GetInstanceID()})";
    }

    private void DisposeGlobalServiceScope()
    {
        DisposeSceneRuntimeScopes();
        DisposeGlobalServicesPublication();

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

    private void DisposeGlobalServicesPublication()
    {
        GlobalServicePublication publication = globalServicesPublication;
        globalServicesPublication = null;

        if (publication == null)
            return;

        try
        {
            publication.Dispose();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
    }

    private void DisposeSceneRuntimeScopes()
    {
        try
        {
            sceneRuntimeScopes?.Dispose();
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

    internal bool IsGameMapScene(Scene scene)
    {
        if (globalServiceScope == null || globalServiceScope.IsDisposed)
            return false;

        return globalServiceScope.TryResolve(out IGameMapCatalog mapCatalog) &&
               mapCatalog.TryGetMap(scene.name, scene.path, out _);
    }

    internal bool ShouldActivateSceneScope(Scene scene)
    {
        return gameMapService == null || gameMapService.ShouldActivateSceneScope(scene);
    }

    private static string GetSceneLabel(Scene scene)
    {
        if (!string.IsNullOrWhiteSpace(scene.path))
            return scene.path;

        if (!string.IsNullOrWhiteSpace(scene.name))
            return scene.name;

        return $"handle {scene.handle}";
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
