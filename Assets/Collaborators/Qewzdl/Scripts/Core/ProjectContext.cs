using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-1100)]
[DisallowMultipleComponent]
public sealed class ProjectContext : MonoBehaviour
{
    private static ProjectContext instance;

    [Header("Project")]
    [SerializeField] private ProjectSettings settings;
    [SerializeField] private ProjectSceneFlow sceneFlow;

    [Header("Network")]
    [SerializeField] private NetworkManager networkManager;

    [Header("Services")]
    [SerializeField] private GameStateMachine stateMachine;
    [SerializeField] private NetworkSessionOrchestrator sessionOrchestrator;
    [SerializeField] private NetworkConnectionService connectionService;
    [SerializeField] private LocalSceneLoader localSceneLoader;
    [SerializeField] private NetworkSceneLoader networkSceneLoader;
    [SerializeField] private ProjectSceneNavigator sceneNavigator;
    [SerializeField] private UiErrorManager uiErrorManager;
    [SerializeField] private AudioManager audioManager;

    private bool referencesValidated;

    public static ProjectContext Instance => instance;

    public ProjectSettings Settings => settings;
    public ProjectSceneFlow SceneFlow => sceneFlow;

    public NetworkManager NetworkManager
    {
        get
        {
            ResolveReferences();
            return networkManager;
        }
    }

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

    public LocalSceneLoader LocalSceneLoader
    {
        get
        {
            ResolveReferences();
            return localSceneLoader;
        }
    }

    public NetworkSceneLoader NetworkSceneLoader
    {
        get
        {
            ResolveReferences();
            return networkSceneLoader;
        }
    }

    public NetworkSceneLoader SceneLoader => NetworkSceneLoader;

    public ProjectSceneNavigator SceneNavigator
    {
        get
        {
            ResolveReferences();
            return sceneNavigator;
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
        if (localSceneLoader != null)
            localSceneLoader.Construct(this);

        if (networkSceneLoader != null)
            networkSceneLoader.Construct(this);

        if (sceneNavigator != null)
            sceneNavigator.Construct(this, localSceneLoader, networkSceneLoader);

        if (referencesValidated)
            return;

        referencesValidated = true;

        ValidateRequiredReference(settings, nameof(settings));
        ValidateRequiredReference(sceneFlow, nameof(sceneFlow));
        ValidateRequiredReference(networkManager, nameof(networkManager));
        ValidateRequiredReference(stateMachine, nameof(stateMachine));
        ValidateRequiredReference(sessionOrchestrator, nameof(sessionOrchestrator));
        ValidateRequiredReference(connectionService, nameof(connectionService));
        ValidateRequiredReference(localSceneLoader, nameof(localSceneLoader));
        ValidateRequiredReference(networkSceneLoader, nameof(networkSceneLoader));
        ValidateRequiredReference(sceneNavigator, nameof(sceneNavigator));
        ValidateRequiredReference(uiErrorManager, nameof(uiErrorManager));
        ValidateRequiredReference(audioManager, nameof(audioManager));
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

    public bool TryGetScene(ProjectSceneKind sceneKind, out ProjectSceneDefinition scene)
    {
        if (settings != null && settings.TryGetScene(sceneKind, out scene))
            return true;

        return ProjectSettings.TryGetDefaultScene(sceneKind, out scene);
    }

    private void ValidateRequiredReference(Object reference, string fieldName)
    {
        if (reference != null)
            return;

        Debug.LogError($"{nameof(ProjectContext)} is missing '{fieldName}'.", this);
    }
}