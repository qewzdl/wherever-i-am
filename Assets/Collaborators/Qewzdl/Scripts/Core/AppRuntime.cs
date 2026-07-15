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

    [Header("Startup")]
    [SerializeField] private bool loadStartupScene = true;

    [Header("Context")]
    [SerializeField] private ProjectContext context;

    private readonly SceneRuntimeScopeRegistry sceneScopes = new();
    private ProjectSceneKind startupSceneOverride = ProjectSceneKind.Unknown;
    private bool runtimeStarted;
    private bool sceneEventsSubscribed;
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
            return context != null && context.IsReady;

        if (!EnsureContext())
            return false;

        if (!context.ConfigureSceneRuntimeScopes(sceneScopes))
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
