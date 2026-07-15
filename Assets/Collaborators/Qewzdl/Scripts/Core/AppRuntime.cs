using System;
using System.Collections;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class AppRuntime : MonoBehaviour, IProjectSceneLoadCompletionGate
{
    public const string EditorStartupScenePathKey = "WhereverIAm.AppRuntime.EditorStartupScenePath";

    [Header("Startup")]
    [SerializeField] private bool loadStartupScene = true;

    [Header("Context")]
    [SerializeField] private ProjectContext context;

    [Header("Client Readiness")]
    [SerializeField] [Min(1f)] private float clientReadinessTimeoutSeconds = 15f;

    private readonly SceneRuntimeScopeRegistry sceneScopes = new();
    private ProjectSceneKind startupSceneOverride = ProjectSceneKind.Unknown;
    private Task failedSessionShutdownTask = Task.CompletedTask;
    private Action<bool> pendingSceneActivation;
    private ProjectSceneKind pendingSceneActivationKind = ProjectSceneKind.Unknown;
    private Coroutine clientReadinessCoroutine;
    private ProjectSceneKind pendingClientReadinessKind = ProjectSceneKind.Unknown;
    private int pendingClientReadinessSceneHandle;
    private bool runtimeStarted;
    private bool sceneEventsSubscribed;
    private bool sceneActivationHealthy = true;
    public int SceneScopeCount => sceneScopes.Count;

    private void Awake()
    {
        if (!EnsureContext())
            return;

        context.MakePersistent();
        DontDestroyOnLoad(gameObject);
        SubscribeToSceneEvents();
    }

    private void Start()
    {
        StartRuntime();
    }

    private void OnDestroy()
    {
        UnsubscribeFromSceneEvents();

        ShutdownRuntime();
    }

    public void Configure(ProjectContext projectContext, ProjectSceneKind startupScene)
    {
        context = projectContext;
        startupSceneOverride = startupScene;
    }

    public bool StartRuntime()
    {
        if (runtimeStarted)
            return context != null && context.IsReady && sceneActivationHealthy;

        if (!EnsureContext())
            return false;

        if (!context.ConfigureSceneRuntimeScopes(sceneScopes))
            return false;

        if (!context.StartRuntime())
            return false;

        runtimeStarted = true;
        sceneActivationHealthy = true;

        if (!TryActivateScene(SceneManager.GetActiveScene()))
            return false;

        if (loadStartupScene)
            LoadStartupSceneIfNeeded();

        return true;
    }

    public void ShutdownRuntime()
    {
        bool shouldShutdownContext = runtimeStarted;
        DisposeSceneScopes();

        if (!shouldShutdownContext || context == null)
            return;

        context.ShutdownRuntime();
        context.DisposeRuntime();
    }

    private void DisposeSceneScopes()
    {
        runtimeStarted = false;
        sceneActivationHealthy = true;
        ClearPendingSceneActivation();
        CancelClientReadiness();
        sceneScopes.Dispose();
    }

    public void LoadScene(ProjectSceneKind sceneKind)
    {
        if (sceneKind == ProjectSceneKind.Unknown)
        {
            Debug.LogError($"{nameof(AppRuntime)} cannot load unknown scene.", this);
            return;
        }

        if (!TryGetSceneNavigator(out ProjectSceneNavigator navigator))
            return;

        navigator.LoadLocal(sceneKind);
    }

    private bool EnsureContext()
    {
        if (context != null)
            return true;

        Debug.LogError($"{nameof(AppRuntime)} is missing {nameof(ProjectContext)}.", this);
        enabled = false;
        return false;
    }

    private void SubscribeToSceneEvents()
    {
        if (sceneEventsSubscribed)
            return;

        SceneManager.sceneLoaded += HandleSceneLoaded;
        SceneManager.sceneUnloaded += HandleSceneUnloaded;
        sceneEventsSubscribed = true;
    }

    private void UnsubscribeFromSceneEvents()
    {
        if (!sceneEventsSubscribed)
            return;

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        sceneEventsSubscribed = false;
    }

    private void LoadStartupSceneIfNeeded()
    {
        Scene activeScene = SceneManager.GetActiveScene();

        if (!context.IsScene(context.GetBootstrapSceneKind(), activeScene.name))
            return;

        ProjectSceneKind startupScene = ResolveStartupScene();

        if (startupScene == ProjectSceneKind.Unknown)
        {
            Debug.LogError("Startup scene is not configured.", this);
            return;
        }

        if (startupScene == context.GetBootstrapSceneKind())
            startupScene = context.GetDefaultStartupScene();

        if (context.IsScene(startupScene, activeScene.name))
            return;

        LoadScene(startupScene);
    }

    private ProjectSceneKind ResolveStartupScene()
    {
        if (startupSceneOverride != ProjectSceneKind.Unknown &&
            startupSceneOverride != context.GetBootstrapSceneKind())
        {
            return startupSceneOverride;
        }

        string editorRequestedScenePath = GetEditorRequestedScenePath();

        if (!string.IsNullOrWhiteSpace(editorRequestedScenePath))
        {
            ProjectSceneKind editorSceneKind = context.GetSceneKind(string.Empty, editorRequestedScenePath);

            if (editorSceneKind != ProjectSceneKind.Unknown)
                return editorSceneKind;

            Debug.LogError($"Editor requested unknown startup scene '{editorRequestedScenePath}'.", this);
            return ProjectSceneKind.Unknown;
        }

        return context.GetDefaultStartupScene();
    }

    private static string GetEditorRequestedScenePath()
    {
#if UNITY_EDITOR
        string sceneName = SessionState.GetString(EditorStartupScenePathKey, string.Empty);
        SessionState.EraseString(EditorStartupScenePathKey);
        return sceneName;
#else
        return string.Empty;
#endif
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode loadMode)
    {
        if (!runtimeStarted || context == null || !context.IsReady)
            return;

        TryActivateScene(scene);
    }

    private void HandleSceneUnloaded(Scene scene)
    {
        if (pendingClientReadinessSceneHandle == scene.handle)
            CancelClientReadiness();

        sceneScopes.Uninstall(scene.handle);
    }

    private bool TryActivateScene(Scene scene)
    {
        if (context == null || !scene.IsValid() || !scene.isLoaded)
            return false;

        if (!context.ShouldActivateSceneScope(scene))
            return true;

        ProjectSceneKind sceneKind = context.GetSceneKind(scene.name, scene.path);
        bool isMapScene = context.IsGameMapScene(scene);

        if (ProjectSceneScopePolicy.TryGetRequirements(
                sceneKind,
                isMapScene,
                out ProjectSceneScopeRequirements requirements))
        {
            if (!sceneScopes.Install(scene, context))
            {
                HandleSceneActivationFailure(scene, sceneKind, requirements);
                return false;
            }

            sceneActivationHealthy = true;
            CompletePendingSceneActivation(sceneKind, true);
        }

        if (RequiresClientReadiness(sceneKind))
            return BeginClientReadiness(scene, sceneKind);

        if (!ShouldDeferSceneStateCommit())
            ApplyStateForScene(sceneKind);

        return true;
    }

    private bool RequiresClientReadiness(ProjectSceneKind sceneKind)
    {
        if (sceneKind != ProjectSceneKind.Lobby &&
            sceneKind != ProjectSceneKind.Game)
        {
            return false;
        }

        NetworkManager networkManager = context?.NetworkManager;
        return networkManager != null &&
               networkManager.IsClient &&
               !networkManager.IsServer;
    }

    private bool BeginClientReadiness(
        Scene scene,
        ProjectSceneKind sceneKind)
    {
        if (pendingClientReadinessKind != ProjectSceneKind.Unknown)
        {
            HandleClientReadinessFailure(
                sceneKind,
                $"Client is already waiting for scene " +
                $"'{pendingClientReadinessKind}' readiness.");
            return false;
        }

        NetworkSessionOrchestrator orchestrator = context.SessionOrchestrator;
        string error = orchestrator == null
            ? $"{nameof(NetworkSessionOrchestrator)} is not configured."
            : string.Empty;

        if (orchestrator == null ||
            !orchestrator.TryBeginClientSceneReadiness(
                sceneKind,
                out error))
        {
            HandleClientReadinessFailure(sceneKind, error);
            return false;
        }

        pendingClientReadinessKind = sceneKind;
        pendingClientReadinessSceneHandle = scene.handle;

        if (sceneKind == ProjectSceneKind.Game)
            context.StateMachine?.ChangeState(GameState.LoadingGame);

        clientReadinessCoroutine = StartCoroutine(WaitForClientReadiness());
        return true;
    }

    private IEnumerator WaitForClientReadiness()
    {
        float deadline = Time.realtimeSinceStartup + clientReadinessTimeoutSeconds;

        // Let all NGO OnNetworkSpawn callbacks in the current frame complete.
        yield return null;

        while (pendingClientReadinessKind != ProjectSceneKind.Unknown)
        {
            ProjectSceneKind sceneKind = pendingClientReadinessKind;
            Scene scene = GetLoadedSceneByHandle(
                pendingClientReadinessSceneHandle);
            NetworkManager networkManager = context?.NetworkManager;

            if (!scene.IsValid() || !scene.isLoaded)
            {
                CancelClientReadiness();
                yield break;
            }

            if (networkManager == null ||
                !networkManager.IsClient ||
                !networkManager.IsListening)
            {
                HandleClientReadinessFailure(
                    sceneKind,
                    "Network connection stopped before client readiness commit.",
                    false);
                yield break;
            }

            NetworkSessionOrchestrator orchestrator = context.SessionOrchestrator;
            string readinessError = orchestrator == null
                ? $"{nameof(NetworkSessionOrchestrator)} is not configured."
                : string.Empty;

            if (orchestrator != null &&
                orchestrator.TryCommitClientSceneReadiness(
                    sceneKind,
                    out readinessError))
            {
                FinishClientReadinessTracking();
                ApplyStateForScene(sceneKind);
                yield break;
            }

            if (Time.realtimeSinceStartup >= deadline)
            {
                HandleClientReadinessFailure(
                    sceneKind,
                    readinessError,
                    false);
                yield break;
            }

            yield return null;
        }
    }

    private void CancelClientReadiness()
    {
        Coroutine coroutine = clientReadinessCoroutine;
        FinishClientReadinessTracking();

        if (coroutine != null)
            StopCoroutine(coroutine);
    }

    private void FinishClientReadinessTracking()
    {
        clientReadinessCoroutine = null;
        pendingClientReadinessKind = ProjectSceneKind.Unknown;
        pendingClientReadinessSceneHandle = 0;
    }

    private static Scene GetLoadedSceneByHandle(int sceneHandle)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);

            if (scene.handle == sceneHandle)
                return scene;
        }

        return default;
    }

    private void HandleClientReadinessFailure(
        ProjectSceneKind sceneKind,
        string details,
        bool stopCoroutine = true)
    {
        if (stopCoroutine)
            CancelClientReadiness();
        else
            FinishClientReadinessTracking();

        sceneActivationHealthy = false;

        string failureDetails = string.IsNullOrWhiteSpace(details)
            ? $"Client readiness timed out for scene '{sceneKind}'."
            : $"Client readiness failed for scene '{sceneKind}': {details}";

        Debug.LogError(failureDetails, this);
        context?.SceneFlowService?.CancelPendingOperations(
            ProjectOperationCancelReason.SessionServiceReadinessLost);
        context?.StateMachine?.ChangeState(GameState.Error);

        if (!failedSessionShutdownTask.IsCompleted)
            return;

        ConnectionResult failure = ConnectionResult.Fail(
            ConnectionErrorCode.SessionServiceReadinessFailed,
            "Failed to synchronize required network session services.",
            failureDetails,
            true);

        failedSessionShutdownTask = ShutdownFailedSessionAsync(failure);
    }

    private bool ShouldDeferSceneStateCommit()
    {
        IProjectSceneFlowService sceneFlowService = context?.SceneFlowService;
        return sceneFlowService != null && sceneFlowService.HasPendingOperation;
    }

    private void ApplyStateForScene(ProjectSceneKind sceneKind)
    {
        if (sceneKind == ProjectSceneKind.Unknown)
            return;

        GameStateMachine stateMachine = context.StateMachine;

        if (stateMachine == null)
            return;

        GameState sceneState = context.GetStateForScene(sceneKind);
        stateMachine.ChangeState(sceneState);
    }

    private void HandleSceneActivationFailure(
        Scene scene,
        ProjectSceneKind sceneKind,
        ProjectSceneScopeRequirements requirements)
    {
        sceneActivationHealthy = false;
        string sceneLabel = GetSceneLabel(scene);

        Debug.LogError(
            $"Scene scope activation failed for '{sceneLabel}' " +
            $"({sceneKind}, handle {scene.handle}). Gameplay activation was blocked.",
            this);

        context.SceneFlowService?.CancelPendingOperations(
            ProjectOperationCancelReason.SceneScopeActivationFailed);
        context.StateMachine?.ChangeState(GameState.Error);

        if (requirements.Parent != SceneServiceScopeParent.Session)
            return;

        if (!failedSessionShutdownTask.IsCompleted)
            return;

        ConnectionResult failure = ConnectionResult.Fail(
            ConnectionErrorCode.SceneScopeActivationFailed,
            "Failed to initialize the game scene.",
            $"Required scene scope activation failed for '{sceneLabel}' " +
            $"({sceneKind}, handle {scene.handle}).",
            true);

        failedSessionShutdownTask = ShutdownFailedSessionAsync(failure);
    }

    bool IProjectSceneLoadCompletionGate.CanHandle(ProjectSceneKind sceneKind)
    {
        return ProjectSceneScopePolicy.TryGetRequirements(
            sceneKind,
            false,
            out _);
    }

    bool IProjectSceneLoadCompletionGate.Validate(
        ProjectSceneKind sceneKind,
        out string error)
    {
        if (!ProjectSceneScopePolicy.TryGetRequirements(sceneKind, false, out _))
        {
            error = $"{nameof(AppRuntime)} cannot gate scene kind '{sceneKind}'.";
            return false;
        }

        if (context == null || !runtimeStarted || !context.IsReady)
        {
            error = $"{nameof(AppRuntime)} is not ready to activate scene '{sceneKind}'.";
            return false;
        }

        if (pendingSceneActivation != null)
        {
            error = $"{nameof(AppRuntime)} is already waiting for scene " +
                    $"'{pendingSceneActivationKind}' activation.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    bool IProjectSceneLoadCompletionGate.BeginWait(
        ProjectSceneKind sceneKind,
        Action<bool> completed)
    {
        if (completed == null ||
            !ProjectSceneScopePolicy.TryGetRequirements(sceneKind, false, out _))
        {
            return false;
        }

        if (pendingSceneActivation != null)
        {
            Debug.LogError(
                $"{nameof(AppRuntime)} is already waiting for scene " +
                $"'{pendingSceneActivationKind}' activation.",
                this);

            return false;
        }

        if (IsProjectSceneScopeReady(sceneKind))
        {
            completed(true);
            return true;
        }

        pendingSceneActivationKind = sceneKind;
        pendingSceneActivation = completed;
        return true;
    }

    void IProjectSceneLoadCompletionGate.CancelPending(ProjectOperationCancelReason reason)
    {
        ClearPendingSceneActivation();
    }

    private bool IsProjectSceneScopeReady(ProjectSceneKind sceneKind)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);

            if (!scene.IsValid() || !scene.isLoaded ||
                context.GetSceneKind(scene.name, scene.path) != sceneKind)
            {
                continue;
            }

            if (sceneScopes.TryGetScope(scene.handle, out SceneRuntimeScope scope) &&
                scope.IsReady)
            {
                return true;
            }
        }

        return false;
    }

    private void CompletePendingSceneActivation(
        ProjectSceneKind sceneKind,
        bool success)
    {
        if (pendingSceneActivation == null ||
            pendingSceneActivationKind != sceneKind)
        {
            return;
        }

        Action<bool> completed = pendingSceneActivation;
        pendingSceneActivation = null;
        pendingSceneActivationKind = ProjectSceneKind.Unknown;
        completed(success);
    }

    private void ClearPendingSceneActivation()
    {
        pendingSceneActivation = null;
        pendingSceneActivationKind = ProjectSceneKind.Unknown;
    }

    private async Task ShutdownFailedSessionAsync(ConnectionResult failure)
    {
        try
        {
            NetworkSessionOrchestrator sessionOrchestrator = context.SessionOrchestrator;

            if (sessionOrchestrator == null)
                throw new InvalidOperationException(
                    $"{nameof(NetworkSessionOrchestrator)} is not configured.");

            await sessionOrchestrator.ShutdownAfterFailureAsync(failure);
        }
        catch (Exception exception)
        {
            context?.StateMachine?.ChangeState(GameState.Error);
            Debug.LogError("Failed to shut down a session after activation failure.", this);
            Debug.LogException(exception, this);
        }
    }

    private static string GetSceneLabel(Scene scene)
    {
        if (!string.IsNullOrWhiteSpace(scene.path))
            return scene.path;

        if (!string.IsNullOrWhiteSpace(scene.name))
            return scene.name;

        return $"handle {scene.handle}";
    }

    private bool TryGetSceneNavigator(out ProjectSceneNavigator navigator)
    {
        navigator = null;

        if (!EnsureContext())
            return false;

        navigator = context.SceneNavigator;

        if (navigator != null)
            return true;

        Debug.LogError($"{nameof(AppRuntime)} is missing {nameof(ProjectSceneNavigator)}.", this);
        return false;
    }
}
