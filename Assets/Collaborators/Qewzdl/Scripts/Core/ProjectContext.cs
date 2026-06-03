using Unity.Netcode;
using UnityEngine;

[DefaultExecutionOrder(-1100)]
[DisallowMultipleComponent]
public sealed class ProjectContext : MonoBehaviour
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

    private bool referencesValidated;
    private bool referenceValidationFailureLogged;
    private bool sceneServicesComposing;
    private bool sceneServicesComposed;

    public static ProjectContext Instance => instance;

    public ProjectSceneRegistry SceneRegistry
    {
        get
        {
            ResolveReferences();
            return sceneRegistry;
        }
    }

    public ProjectSettings Settings
    {
        get
        {
            ResolveReferences();
            return sceneRegistry != null ? sceneRegistry.Settings : null;
        }
    }

    public ProjectSceneFlow SceneFlow
    {
        get
        {
            ResolveReferences();
            return sceneRegistry != null ? sceneRegistry.SceneFlow : null;
        }
    }

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

    public NetworkConnectionApprovalService ConnectionApprovalService
    {
        get
        {
            ResolveReferences();
            return connectionApprovalService;
        }
    }

    public LocalSceneLoader LocalSceneLoader
    {
        get
        {
            ResolveReferences();
            return sceneServiceComposer != null ? sceneServiceComposer.LocalSceneLoader : null;
        }
    }

    public NetworkSceneLoader NetworkSceneLoader
    {
        get
        {
            ResolveReferences();
            return sceneServiceComposer != null ? sceneServiceComposer.NetworkSceneLoader : null;
        }
    }

    public NetworkSceneLoader SceneLoader => NetworkSceneLoader;

    public ProjectSceneNavigator SceneNavigator
    {
        get
        {
            ResolveReferences();
            return sceneServiceComposer != null ? sceneServiceComposer.SceneNavigator : null;
        }
    }

    public ProjectSceneFlowService SceneFlowService
    {
        get
        {
            ResolveReferences();
            return sceneServiceComposer != null ? sceneServiceComposer.SceneFlowService : null;
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

    public bool ResolveReferences()
    {
        bool referencesReady = ValidateReferences();

        if (!referencesReady)
            return false;

        return ComposeSceneServices();
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

        referencesValidated = valid;
        referenceValidationFailureLogged = !valid;

        return valid;
    }

    private bool ComposeSceneServices()
    {
        if (sceneServicesComposed)
            return true;

        if (sceneServicesComposing)
            return false;

        if (sceneServiceComposer == null)
            return false;

        sceneServicesComposing = true;

        try
        {
            sceneServicesComposed = sceneServiceComposer.Compose(this);
            return sceneServicesComposed;
        }
        finally
        {
            sceneServicesComposing = false;
        }
    }

    private bool ValidateRequiredReference(Object reference, string fieldName, bool logError)
    {
        if (reference != null)
            return true;

        if (logError)
            Debug.LogError($"{nameof(ProjectContext)} is missing '{fieldName}'.", this);

        return false;
    }
}
