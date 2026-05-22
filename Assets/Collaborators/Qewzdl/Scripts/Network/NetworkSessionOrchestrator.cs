using System.Collections;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

public class NetworkSessionOrchestrator : MonoBehaviour, INetworkSessionService
{
    public static NetworkSessionOrchestrator Instance { get; private set; }

    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private GameStateMachine stateMachine;
    [SerializeField] private NetworkConnectionService connectionService;
    [SerializeField] private ProjectSceneFlowService sceneFlowService;
    [SerializeField] private ProjectContext projectContext;

    private bool networkCallbacksSubscribed;

    private NetworkManager subscribedNetworkManager;
    private Coroutine shutdownRoutine;

    private IUiErrorService errorService;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();

        SubscribeToNetworkCallbacks();
    }

    private void OnDisable()
    {
        UnsubscribeFromNetworkCallbacks();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public async Task HostLanAsync()
    {
        if (!HasRequiredReferences())
            return;

        if (TryGetErrorService(out IUiErrorService service))
            service.HideError();

        if (connectionService.IsListening)
        {
            Debug.LogWarning("Network is already running.");
            return;
        }

        stateMachine.ChangeState(GameState.Connecting);

        ConnectionResult result = await connectionService.StartHostAsync();

        if (!result.Success)
        {
            FailAndReturnToMainMenu(result);
            return;
        }

        RefreshNetworkSubscriptions();

        if (!sceneFlowService.LoadScene(ProjectSceneKind.Lobby))
        {
            FailAndReturnToMainMenu(ConnectionResult.Fail(
                ConnectionErrorCode.LobbySceneLoadFailed,
                "Failed to load the lobby.",
                "Failed to load lobby scene.",
                true
            ));
        }
    }

    public async Task JoinLanAsync(string ip)
    {
        if (!HasRequiredReferences())
            return;

        if (TryGetErrorService(out IUiErrorService service))
            service.HideError();

        stateMachine.ChangeState(GameState.Connecting);

        ConnectionResult result = await connectionService.StartClientAsync(ip);

        if (!result.Success)
        {
            FailAndReturnToMainMenu(result);
            return;
        }

        RefreshNetworkSubscriptions();

        Debug.Log(result.DebugMessage);
    }

    public void StartGame()
    {
        if (!HasRequiredReferences())
            return;

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager.Singleton is null.");
            return;
        }

        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("Only server can start the game.");
            return;
        }

        sceneFlowService.LoadScene(ProjectSceneKind.Game);
    }

    public void ShutdownToMainMenu()
    {
        if (!HasRequiredReferences())
            return;

        if (shutdownRoutine != null)
            return;

        shutdownRoutine = StartCoroutine(ShutdownToMainMenuRoutine());
    }

    private IEnumerator ShutdownToMainMenuRoutine()
    {
        stateMachine.ChangeState(GameState.Disconnecting);

        ResetNetworkSubscriptions();

        connectionService.Shutdown();

        yield return null;

        sceneFlowService.LoadScene(ProjectSceneKind.MainMenu);

        shutdownRoutine = null;
    }

    private void RefreshNetworkSubscriptions()
    {
        ResetNetworkSubscriptions();

        ResolveReferences();

        SubscribeToNetworkCallbacks();
    }

    private void ResetNetworkSubscriptions()
    {
        UnsubscribeFromNetworkCallbacks();
    }

    private void ResolveReferences()
    {
        if (projectContext == null)
            projectContext = ProjectContext.Instance;

        if (networkManager == null)
            networkManager = GetComponent<NetworkManager>();

        if (networkManager == null)
            networkManager = NetworkManager.Singleton;

        if (stateMachine == null && projectContext != null)
            stateMachine = projectContext.StateMachine;

        if (connectionService == null && projectContext != null)
            connectionService = projectContext.ConnectionService;

        if (sceneFlowService == null && projectContext != null)
            sceneFlowService = projectContext.SceneFlowService;

        if (errorService == null)
        {
            if (UiErrorManager.TryGetInstance(out UiErrorManager uiErrorManager))
                errorService = uiErrorManager;
        }
    }

    private bool HasRequiredReferences()
    {
        ResolveReferences();

        if (stateMachine == null)
        {
            Debug.LogError("GameStateMachine reference is missing.");
            return false;
        }

        if (connectionService == null)
        {
            Debug.LogError("NetworkConnectionService reference is missing.");
            return false;
        }

        if (sceneFlowService == null)
        {
            Debug.LogError("ProjectSceneFlowService reference is missing.");
            return false;
        }

        return true;
    }

    private void SubscribeToNetworkCallbacks()
    {
        if (networkManager == null)
            return;

        if (networkCallbacksSubscribed && subscribedNetworkManager == networkManager)
            return;

        UnsubscribeFromNetworkCallbacks();

        subscribedNetworkManager = networkManager;
        subscribedNetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

        networkCallbacksSubscribed = true;
    }

    private void UnsubscribeFromNetworkCallbacks()
    {
        if (!networkCallbacksSubscribed)
            return;

        if (subscribedNetworkManager != null)
            subscribedNetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;

        subscribedNetworkManager = null;
        networkCallbacksSubscribed = false;
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (networkManager == null)
            return;

        if (clientId != networkManager.LocalClientId)
            return;

        if (stateMachine == null)
            return;

        if (stateMachine.CurrentState == GameState.Disconnecting)
            return;

        if (stateMachine.CurrentState == GameState.Connecting)
        {
            FailAndReturnToMainMenu("Connection failed or was interrupted while connecting.");
            return;
        }

        if (stateMachine.CurrentState == GameState.Lobby ||
            stateMachine.CurrentState == GameState.LoadingGame ||
            stateMachine.CurrentState == GameState.InGame)
        {
            FailAndReturnToMainMenu("Disconnected from network session.");
        }
    }

    private void FailAndReturnToMainMenu(string userMessage, string debugMessage = "")
    {
        ConnectionResult result = ConnectionResult.Fail(
            ConnectionErrorCode.Unknown,
            userMessage,
            string.IsNullOrWhiteSpace(debugMessage) ? userMessage : debugMessage,
            true
        );

        FailAndReturnToMainMenu(result);
    }

    private void FailAndReturnToMainMenu(ConnectionResult result)
    {
        Debug.LogWarning(result.DebugMessage);

        if (stateMachine != null)
            stateMachine.ChangeState(GameState.Error);

        ResetNetworkSubscriptions();

        if (connectionService != null)
            connectionService.Shutdown();

        if (sceneFlowService != null)
            sceneFlowService.LoadScene(ProjectSceneKind.MainMenu);

        if (TryGetErrorService(out IUiErrorService service))
            service.ShowError(result.UserMessage);
    }

    private bool TryGetErrorService(out IUiErrorService service)
    {
        service = errorService;

        if (service != null)
            return true;

        if (!UiErrorManager.TryGetInstance(out UiErrorManager uiErrorManager))
            return false;

        errorService = uiErrorManager;
        service = errorService;

        return true;
    }
}