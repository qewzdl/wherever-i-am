using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class AppRuntime : MonoBehaviour
{
    public const string EditorStartupScenePathKey = "WhereverIAm.AppRuntime.EditorStartupScenePath";

    private static AppRuntime instance;

    [Header("Startup")]
    [SerializeField] private bool loadStartupScene = true;

    [Header("Context")]
    [SerializeField] private ProjectContext context;

    private readonly SceneRuntimeScopeRegistry sceneScopes = new();
    private ProjectSceneKind startupSceneOverride = ProjectSceneKind.Unknown;
    private bool runtimeStarted;
    private bool sceneEventsSubscribed;
    private bool ownsRuntime;

    public static AppRuntime Instance => instance;
    public bool IsRuntimeStarted => runtimeStarted;
    public int SceneScopeCount => sceneScopes.Count;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        ownsRuntime = true;

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

        if (!ownsRuntime)
            return;

        ShutdownRuntime();

        if (instance == this)
            instance = null;
    }

    public void Configure(ProjectContext projectContext, ProjectSceneKind startupScene)
    {
        context = projectContext;
        startupSceneOverride = startupScene;
    }

    public bool StartRuntime()
    {
        if (runtimeStarted)
            return context != null && context.IsReady;

        if (!EnsureContext())
            return false;

        if (!context.StartRuntime())
            return false;

        runtimeStarted = true;

        ApplyStateForScene(SceneManager.GetActiveScene());
        sceneScopes.Install(SceneManager.GetActiveScene(), context);

        if (loadStartupScene)
            LoadStartupSceneIfNeeded();

        return true;
    }

    public void ShutdownRuntime()
    {
        DisposeSceneScopes();

        if (context == null)
            return;

        context.ShutdownRuntime();
        context.DisposeRuntime();
    }

    internal void DisposeSceneScopes(ProjectContext projectContext = null)
    {
        if (projectContext != null && projectContext != context)
            return;

        runtimeStarted = false;
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

        ApplyStateForScene(scene);
        sceneScopes.Install(scene, context);
    }

    private void HandleSceneUnloaded(Scene scene)
    {
        sceneScopes.Uninstall(scene.handle);
    }

    internal bool InstallSceneScope(Scene scene, ProjectContext projectContext)
    {
        if (!runtimeStarted || context == null || !context.IsReady)
        {
            Debug.LogError(
                $"Cannot install scene scope '{scene.name}' before {nameof(AppRuntime)} is ready.",
                this);

            return false;
        }

        if (projectContext != context)
        {
            Debug.LogError(
                $"Cannot install scene scope '{scene.name}' with another {nameof(ProjectContext)}.",
                this);

            return false;
        }

        return sceneScopes.Install(scene, context);
    }

    internal bool UninstallSceneScope(int sceneHandle)
    {
        return sceneScopes.Uninstall(sceneHandle);
    }

    private void ApplyStateForScene(Scene scene)
    {
        if (context == null)
            return;

        ProjectSceneKind sceneKind = context.GetSceneKind(scene.name, scene.path);

        if (sceneKind == ProjectSceneKind.Unknown)
            return;

        GameStateMachine stateMachine = context.StateMachine;

        if (stateMachine == null)
            return;

        GameState sceneState = context.GetStateForScene(sceneKind);
        stateMachine.ChangeState(sceneState);
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
