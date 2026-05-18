using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
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

    private ProjectSceneKind startupSceneOverride = ProjectSceneKind.Unknown;
    private bool runtimeStarted;
    private bool sceneEventsSubscribed;

    public static AppRuntime Instance
    {
        get
        {
            if (instance != null)
                return instance;

            instance = FindFirstObjectByType<AppRuntime>();
            return instance;
        }
    }

    public static AppRuntime GetOrCreateOn(GameObject host)
    {
        if (Instance != null)
            return instance;

        if (host == null)
            host = new GameObject(nameof(AppRuntime));

        if (host.TryGetComponent(out AppRuntime runtime))
            return runtime;

        return host.AddComponent<AppRuntime>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;

        EnsureContext();
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

        if (instance == this)
            instance = null;
    }

    public void Configure(ProjectContext projectContext, ProjectSceneKind startupScene)
    {
        context = projectContext;
        startupSceneOverride = startupScene;
    }

    public void StartRuntime()
    {
        if (runtimeStarted)
            return;

        runtimeStarted = true;

        EnsureContext();
        context.ResolveReferences();
        ApplyStateForScene(SceneManager.GetActiveScene());
        SceneRuntime.InstallActiveScene(context);

        if (loadStartupScene)
            LoadStartupSceneIfNeeded();
    }

    private void EnsureContext()
    {
        if (context != null)
            return;

        context = ProjectContext.GetOrCreateOn(gameObject);
    }

    private void SubscribeToSceneEvents()
    {
        if (sceneEventsSubscribed)
            return;

        SceneManager.sceneLoaded += HandleSceneLoaded;
        sceneEventsSubscribed = true;
    }

    private void UnsubscribeFromSceneEvents()
    {
        if (!sceneEventsSubscribed)
            return;

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        sceneEventsSubscribed = false;
    }

    private void LoadStartupSceneIfNeeded()
    {
        Scene activeScene = SceneManager.GetActiveScene();

        if (!context.IsScene(context.GetBootstrapSceneKind(), activeScene.name))
            return;

        ProjectSceneKind startupScene = ResolveStartupScene();

        if (startupScene == ProjectSceneKind.Unknown ||
            startupScene == context.GetBootstrapSceneKind())
        {
            startupScene = context.GetDefaultStartupScene();
        }

        string sceneName = context.GetSceneName(startupScene);

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError($"Startup scene is not configured for {startupScene}.");
            return;
        }

        if (context.IsScene(startupScene, activeScene.name))
            return;

        LoadScene(startupScene);
    }

    public void LoadScene(ProjectSceneKind sceneKind)
    {
        if (sceneKind == ProjectSceneKind.Unknown)
            return;

        EnsureContext();

        string sceneName = context.GetSceneName(sceneKind);
        string scenePath = context.GetScenePath(sceneKind);

        if (string.IsNullOrWhiteSpace(sceneName) && string.IsNullOrWhiteSpace(scenePath))
        {
            Debug.LogError($"Scene is not configured for {sceneKind}.");
            return;
        }

        LoadConfiguredScene(sceneName, scenePath);
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

            Debug.LogWarning($"Editor requested unknown startup scene '{editorRequestedScenePath}'. Falling back to {context.GetDefaultStartupScene()}.");
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
        ApplyStateForScene(scene);
        SceneRuntime.InstallScene(scene, context);
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

    private static void LoadConfiguredScene(string sceneName, string scenePath)
    {
#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(scenePath))
        {
            EditorSceneManager.LoadSceneInPlayMode(
                scenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
            return;
        }
#endif

        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }
}
