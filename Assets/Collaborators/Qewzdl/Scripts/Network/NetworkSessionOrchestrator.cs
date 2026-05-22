using System.Collections;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

public class NetworkSessionOrchestrator : MonoBehaviour, INetworkSessionService
{
    private const string LobbyJoinDeniedReason = "The game has already started. You can only join while the host is in the lobby.";

    public static NetworkSessionOrchestrator Instance { get; private set; }

    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private GameStateMachine stateMachine;
    [SerializeField] private NetworkConnectionService connectionService;
    [SerializeField] private ProjectSceneNavigator sceneNavigator;
    [SerializeField] private NetworkChatSessionSpawner chatSessionSpawner;
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
        ConfigureConnectionApproval();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ConfigureConnectionApproval();

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

        if (chatSessionSpawner != null)
        {
            chatSessionSpawner.SpawnForServer();
        }
        else
        {
            Debug.LogWarning("NetworkChatSessionSpawner is missing. Chat will be disabled.");
        }

        if (!sceneNavigator.LoadLobby())
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

        if (sceneNavigator.LoadGame())
            stateMachine.ChangeState(GameState.LoadingGame);
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

        if (chatSessionSpawner != null)
            chatSessionSpawner.DespawnForServer();

        connectionService.Shutdown();

        yield return null;

        if (sceneNavigator != null)
            sceneNavigator.LoadMainMenu();

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

        if (sceneNavigator == null && projectContext != null)
            sceneNavigator = projectContext.SceneNavigator;

        if (errorService == null)
        {
            if (UiErrorManager.TryGetInstance(out UiErrorManager uiErrorManager))
                errorService = uiErrorManager;
        }

        if (chatSessionSpawner == null)
            chatSessionSpawner = GetComponent<NetworkChatSessionSpawner>();
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

        if (sceneNavigator == null)
        {
            Debug.LogError("ProjectSceneNavigator reference is missing.");
            return false;
        }

        return true;
    }

    private void ConfigureConnectionApproval()
    {
        if (networkManager == null || networkManager.IsListening)
            return;

        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.ConnectionApprovalCallback = ApproveLobbyConnectionWithoutPlayerObject;
    }

    private void ApproveLobbyConnectionWithoutPlayerObject(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        response.CreatePlayerObject = false;
        response.Pending = false;

        if (request.ClientNetworkId == NetworkManager.ServerClientId || IsAcceptingRemoteClientConnections())
        {
            response.Approved = true;
            return;
        }

        response.Approved = false;
        response.Reason = LobbyJoinDeniedReason;

        Debug.Log($"Rejected client {request.ClientNetworkId}: {LobbyJoinDeniedReason}");
    }

    private bool IsAcceptingRemoteClientConnections()
    {
        if (stateMachine != null)
            return stateMachine.CurrentState == GameState.Lobby;

        if (projectContext != null)
            return projectContext.GetActiveSceneKind() == ProjectSceneKind.Lobby;

        return false;
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

        if (sceneNavigator == null)
        {
            Debug.LogError("ProjectSceneNavigator reference is missing.");
            stateMachine.ChangeState(GameState.Error);
            return;
        }

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

        if (chatSessionSpawner != null)
            chatSessionSpawner.DespawnForServer();

        if (connectionService != null)
            connectionService.Shutdown();

        if (sceneNavigator != null)
            sceneNavigator.LoadMainMenu();

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