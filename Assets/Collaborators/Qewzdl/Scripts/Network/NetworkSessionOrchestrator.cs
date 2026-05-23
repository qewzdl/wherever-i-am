using System.Collections;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class NetworkSessionOrchestrator : MonoBehaviour, INetworkSessionService
{
    public static NetworkSessionOrchestrator Instance { get; private set; }

    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private GameStateMachine stateMachine;
    [SerializeField] private NetworkConnectionService connectionService;
    [SerializeField] private ProjectSceneFlowService sceneFlowService;
    [SerializeField] private UiErrorManager errorManager;

    private bool networkCallbacksSubscribed;
    private NetworkManager subscribedNetworkManager;
    private Coroutine shutdownRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        HasRequiredReferences();
    }

    private void OnEnable()
    {
        if (Instance != this)
            return;

        if (!HasRequiredReferences())
            return;

        SubscribeToNetworkCallbacks();
    }

    private void OnDisable()
    {
        UnsubscribeFromNetworkCallbacks();
    }

    private void OnDestroy()
    {
        UnsubscribeFromNetworkCallbacks();

        if (Instance == this)
            Instance = null;
    }

    public async Task HostLanAsync()
    {
        if (!HasRequiredReferences())
            return;

        errorManager.HideError();

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

        errorManager.HideError();

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

        if (!networkManager.IsServer)
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
        SubscribeToNetworkCallbacks();
    }

    private void ResetNetworkSubscriptions()
    {
        UnsubscribeFromNetworkCallbacks();
    }

    private bool HasRequiredReferences()
    {
        bool hasRequiredReferences = true;

        if (networkManager == null)
        {
            Debug.LogError($"{nameof(NetworkSessionOrchestrator)} is missing {nameof(NetworkManager)} reference.", this);
            hasRequiredReferences = false;
        }

        if (stateMachine == null)
        {
            Debug.LogError($"{nameof(NetworkSessionOrchestrator)} is missing {nameof(GameStateMachine)} reference.", this);
            hasRequiredReferences = false;
        }

        if (connectionService == null)
        {
            Debug.LogError($"{nameof(NetworkSessionOrchestrator)} is missing {nameof(NetworkConnectionService)} reference.", this);
            hasRequiredReferences = false;
        }

        if (sceneFlowService == null)
        {
            Debug.LogError($"{nameof(NetworkSessionOrchestrator)} is missing {nameof(ProjectSceneFlowService)} reference.", this);
            hasRequiredReferences = false;
        }

        if (errorManager == null)
        {
            Debug.LogError($"{nameof(NetworkSessionOrchestrator)} is missing {nameof(UiErrorManager)} reference.", this);
            hasRequiredReferences = false;
        }

        return hasRequiredReferences;
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

        if (errorManager != null)
            errorManager.ShowError(result.UserMessage);
    }
}