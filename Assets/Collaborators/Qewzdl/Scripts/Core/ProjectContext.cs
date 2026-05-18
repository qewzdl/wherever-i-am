using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-1100)]
[DisallowMultipleComponent]
public sealed class ProjectContext : MonoBehaviour
{
    private static ProjectContext instance;

    [Header("Project")]
    [SerializeField] private ProjectSettings settings;

    [Header("Services")]
    [SerializeField] private GameStateMachine stateMachine;
    [SerializeField] private NetworkSessionOrchestrator sessionOrchestrator;
    [SerializeField] private NetworkConnectionService connectionService;
    [SerializeField] private NetworkSceneLoader sceneLoader;
    [SerializeField] private UiErrorManager uiErrorManager;
    [SerializeField] private AudioManager audioManager;

    public static ProjectContext Instance
    {
        get
        {
            if (instance != null)
                return instance;

            instance = FindFirstObjectByType<ProjectContext>();
            return instance;
        }
    }

    public ProjectSettings Settings => settings;

    public INetworkSessionService SessionService => SessionOrchestrator;

    public GameStateMachine StateMachine
    {
        get
        {
            ResolveReferences();
            return stateMachine;
        }
    }

    public NetworkSessionOrchestrator SessionOrchestrator
    {
        get
        {
            ResolveReferences();
            return sessionOrchestrator;
        }
    }

    public NetworkConnectionService ConnectionService
    {
        get
        {
            ResolveReferences();
            return connectionService;
        }
    }

    public NetworkSceneLoader SceneLoader
    {
        get
        {
            ResolveReferences();
            return sceneLoader;
        }
    }

    public UiErrorManager UiErrors
    {
        get
        {
            ResolveReferences();
            return uiErrorManager;
        }
    }

    public AudioManager Audio
    {
        get
        {
            ResolveReferences();
            return audioManager;
        }
    }

    public static ProjectContext GetOrCreateOn(GameObject host)
    {
        if (Instance != null)
            return instance;

        if (host == null)
            host = new GameObject(nameof(ProjectContext));

        if (host.TryGetComponent(out ProjectContext context))
            return context;

        return host.AddComponent<ProjectContext>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        ResolveReferences();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public void MakePersistent()
    {
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    public void ResolveReferences()
    {
        if (stateMachine == null)
            stateMachine = GetComponent<GameStateMachine>();

        if (stateMachine == null)
            stateMachine = FindFirstObjectByType<GameStateMachine>();

        if (sessionOrchestrator == null)
            sessionOrchestrator = GetComponent<NetworkSessionOrchestrator>();

        if (sessionOrchestrator == null)
            sessionOrchestrator = NetworkSessionOrchestrator.Instance != null
                ? NetworkSessionOrchestrator.Instance
                : FindFirstObjectByType<NetworkSessionOrchestrator>();

        if (connectionService == null)
            connectionService = GetComponent<NetworkConnectionService>();

        if (connectionService == null)
            connectionService = FindFirstObjectByType<NetworkConnectionService>();

        if (sceneLoader == null)
            sceneLoader = GetComponent<NetworkSceneLoader>();

        if (sceneLoader == null)
            sceneLoader = FindFirstObjectByType<NetworkSceneLoader>();

        if (uiErrorManager == null)
            uiErrorManager = global::UiErrorManager.Instance;

        if (uiErrorManager == null)
            uiErrorManager = FindFirstObjectByType<UiErrorManager>();

        if (audioManager == null)
            audioManager = global::AudioManager.Instance;

        if (audioManager == null)
            audioManager = FindFirstObjectByType<AudioManager>();
    }

    public string GetSceneName(ProjectSceneKind sceneKind)
    {
        return TryGetScene(sceneKind, out ProjectSceneDefinition scene)
            ? scene.SceneName
            : string.Empty;
    }

    public string GetScenePath(ProjectSceneKind sceneKind)
    {
        return TryGetScene(sceneKind, out ProjectSceneDefinition scene)
            ? scene.ScenePath
            : string.Empty;
    }

    public ProjectSceneKind GetActiveSceneKind()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        return GetSceneKind(activeScene.name, activeScene.path);
    }

    public ProjectSceneKind GetSceneKind(string sceneName)
    {
        return GetSceneKind(sceneName, string.Empty);
    }

    public ProjectSceneKind GetSceneKind(string sceneName, string scenePath)
    {
        if (settings != null)
        {
            ProjectSceneKind configuredKind = settings.GetSceneKind(sceneName, scenePath);

            if (configuredKind != ProjectSceneKind.Unknown)
                return configuredKind;
        }

        return ProjectSettings.GetDefaultSceneKind(sceneName, scenePath);
    }

    public bool IsScene(ProjectSceneKind sceneKind, string sceneName)
    {
        return GetSceneKind(sceneName) == sceneKind;
    }

    public ProjectSceneKind GetBootstrapSceneKind()
    {
        return settings != null
            ? settings.BootstrapScene
            : ProjectSceneKind.Bootstrap;
    }

    public ProjectSceneKind GetDefaultStartupScene()
    {
        return settings != null
            ? settings.DefaultStartupScene
            : ProjectSceneKind.MainMenu;
    }

    public bool CanStartDirectly(string scenePath)
    {
        return settings != null
            ? settings.CanStartDirectly(scenePath)
            : ProjectSettings.CanDefaultSceneStartDirectly(scenePath);
    }

    public GameState GetStateForScene(ProjectSceneKind sceneKind)
    {
        GameState fallback = stateMachine != null
            ? stateMachine.CurrentState
            : GameState.Bootstrapping;

        return TryGetScene(sceneKind, out ProjectSceneDefinition scene)
            ? scene.State
            : fallback;
    }

    private bool TryGetScene(ProjectSceneKind sceneKind, out ProjectSceneDefinition scene)
    {
        if (settings != null && settings.TryGetScene(sceneKind, out scene))
            return true;

        return ProjectSettings.TryGetDefaultScene(sceneKind, out scene);
    }
}
