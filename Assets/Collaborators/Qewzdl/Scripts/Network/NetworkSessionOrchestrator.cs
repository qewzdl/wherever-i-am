using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkSessionOrchestrator : MonoBehaviour, INetworkSessionService
{
    public static NetworkSessionOrchestrator Instance { get; private set; }

    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private GameStateMachine stateMachine;
    [SerializeField] private NetworkConnectionService connectionService;
    [SerializeField] private NetworkSceneLoader sceneLoader;

    [Header("Player")]
    [SerializeField] private GameObject playerPrefab;

    private bool networkCallbacksSubscribed;
    private bool networkSceneCallbacksSubscribed;
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

        SceneManager.sceneLoaded += HandleSceneLoaded;
        SubscribeToNetworkCallbacks();
        SubscribeToNetworkSceneCallbacks();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        UnsubscribeFromNetworkCallbacks();
        UnsubscribeFromNetworkSceneCallbacks();
    }

    public async Task HostLanAsync()
    {
        if (!HasRequiredReferences()) return;

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

        SubscribeToNetworkCallbacks();
        SubscribeToNetworkSceneCallbacks();

        if (!sceneLoader.LoadLobby())
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
        if (!HasRequiredReferences()) return;

        if (TryGetErrorService(out IUiErrorService service))
            service.HideError();

        stateMachine.ChangeState(GameState.Connecting);

        ConnectionResult result = await connectionService.StartClientAsync(ip);

        if (!result.Success)
        {
            FailAndReturnToMainMenu(result);
            return;
        }

        SubscribeToNetworkCallbacks();
        SubscribeToNetworkSceneCallbacks();

        Debug.Log(result.DebugMessage);
    }

    public void StartGame()
    {
        if (!HasRequiredReferences()) return;

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

        if (sceneLoader.LoadGame())
        {
            stateMachine.ChangeState(GameState.LoadingGame);
        }
    }

    public void ShutdownToMainMenu()
    {
        if (!HasRequiredReferences()) return;

        stateMachine.ChangeState(GameState.Disconnecting);

        connectionService.Shutdown();

        sceneLoader.LoadMainMenu();
    }

    private void ResolveReferences()
    {
        if (networkManager == null)
            networkManager = GetComponent<NetworkManager>();

        if (networkManager == null)
            networkManager = NetworkManager.Singleton;

        if (stateMachine == null)
            stateMachine = GetComponent<GameStateMachine>();

        if (connectionService == null)
            connectionService = GetComponent<NetworkConnectionService>();

        if (sceneLoader == null)
            sceneLoader = GetComponent<NetworkSceneLoader>();

        if (playerPrefab == null && networkManager != null)
            playerPrefab = networkManager.NetworkConfig.PlayerPrefab;

        if (errorService == null)
        {
            if (UiErrorManager.TryGetInstance(out UiErrorManager uiErrorManager))
                errorService = uiErrorManager;
        }
    }

    private bool HasRequiredReferences()
    {
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

        if (sceneLoader == null)
        {
            Debug.LogError("NetworkSceneLoader reference is missing.");
            return false;
        }

        return true;
    }

    private void ConfigureConnectionApproval()
    {
        if (networkManager == null || networkManager.IsListening)
            return;

        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.ConnectionApprovalCallback = ApproveConnectionWithoutPlayerObject;
    }

    private void ApproveConnectionWithoutPlayerObject(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        response.Approved = true;
        response.CreatePlayerObject = false;
        response.Pending = false;
    }

    private void SubscribeToNetworkCallbacks()
    {
        if (networkCallbacksSubscribed)
            return;

        if (networkManager == null)
            return;

        networkManager.OnClientConnectedCallback += HandleClientConnected;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        networkCallbacksSubscribed = true;
    }

    private void UnsubscribeFromNetworkCallbacks()
    {
        if (!networkCallbacksSubscribed)
            return;

        if (networkManager != null)
        {
            networkManager.OnClientConnectedCallback -= HandleClientConnected;
            networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        networkCallbacksSubscribed = false;
    }

    private void SubscribeToNetworkSceneCallbacks()
    {
        if (networkSceneCallbacksSubscribed)
            return;

        if (networkManager == null || networkManager.SceneManager == null)
            return;

        networkManager.SceneManager.OnLoadEventCompleted += HandleNetworkLoadEventCompleted;
        networkSceneCallbacksSubscribed = true;
    }

    private void UnsubscribeFromNetworkSceneCallbacks()
    {
        if (!networkSceneCallbacksSubscribed)
            return;

        if (networkManager != null && networkManager.SceneManager != null)
            networkManager.SceneManager.OnLoadEventCompleted -= HandleNetworkLoadEventCompleted;

        networkSceneCallbacksSubscribed = false;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (networkManager == null || sceneLoader == null || !networkManager.IsServer)
            return;

        if (!IsCurrentScene(sceneLoader.GameSceneName))
            return;

        SpawnPlayerForClient(clientId);
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

        if (sceneLoader == null)
        {
            Debug.LogError("NetworkSceneLoader reference is missing.");
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

    private void HandleNetworkLoadEventCompleted(
        string sceneName,
        LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted,
        List<ulong> clientsTimedOut)
    {
        if (sceneLoader == null)
            return;

        if (sceneName != sceneLoader.GameSceneName)
            return;

        if (networkManager == null || !networkManager.IsServer)
            return;

        SpawnPlayersForConnectedClients();
    }

    private void SpawnPlayersForConnectedClients()
    {
        if (networkManager == null || playerPrefab == null)
            return;

        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            SpawnPlayerForClient(clientId);
        }
    }

    private void SpawnPlayerForClient(ulong clientId)
    {
        if (networkManager == null || playerPrefab == null)
            return;

        if (!networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
            return;

        if (client.PlayerObject != null)
            return;

        if (!playerPrefab.TryGetComponent(out NetworkObject playerNetworkObject))
        {
            Debug.LogError("Player prefab is missing NetworkObject.");
            return;
        }

        NetworkObject playerInstance = Instantiate(
            playerNetworkObject,
            playerPrefab.transform.position,
            playerPrefab.transform.rotation);

        playerInstance.SpawnAsPlayerObject(clientId, true);
    }

    private bool IsCurrentScene(string sceneName)
    {
        return SceneManager.GetActiveScene().name == sceneName;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (stateMachine == null || sceneLoader == null)
            return;

        if (scene.name == sceneLoader.MainMenuSceneName)
        {
            stateMachine.ChangeState(GameState.MainMenu);
            return;
        }

        if (scene.name == sceneLoader.LobbySceneName)
        {
            stateMachine.ChangeState(GameState.Lobby);
            return;
        }

        if (scene.name == sceneLoader.GameSceneName)
        {
            stateMachine.ChangeState(GameState.InGame);
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

        if (connectionService != null)
            connectionService.Shutdown();

        if (sceneLoader != null)
            sceneLoader.LoadMainMenu();

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
