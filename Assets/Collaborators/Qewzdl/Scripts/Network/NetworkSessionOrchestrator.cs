using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

public class NetworkSessionOrchestrator : MonoBehaviour
{
    public static NetworkSessionOrchestrator Instance { get; private set; }

    [Header("References")]
    [SerializeField] private GameStateMachine stateMachine;
    [SerializeField] private NetworkConnectionService connectionService;
    [SerializeField] private NetworkSceneLoader sceneLoader;

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

    private void ResolveReferences()
    {
        if (stateMachine == null) stateMachine = GetComponent<GameStateMachine>();

        if (connectionService == null) connectionService = GetComponent<NetworkConnectionService>();

        if (sceneLoader == null) sceneLoader = GetComponent<NetworkSceneLoader>();
    }

    public async Task HostLanAsync()
    {
        if (!HasRequiredReferences()) return;

        if (connectionService.IsListening)
        {
            Debug.LogWarning("Network is already running.");
            return;
        }

        stateMachine.ChangeState(GameState.Connecting);

        ConnectionResult result = await connectionService.StartHostAsync();

        if (!result.Success)
        {
            Debug.LogError(result.Message);
            stateMachine.ChangeState(GameState.Error);
            return;
        }

        stateMachine.ChangeState(GameState.Lobby);
        sceneLoader.LoadLobby();
    }

    public async Task JoinLanAsync(string ip)
    {
        if (!HasRequiredReferences()) return;

        stateMachine.ChangeState(GameState.Connecting);

        ConnectionResult result = await connectionService.StartClientAsync(ip);

        if (!result.Success)
        {
            Debug.LogError(result.Message);
            stateMachine.ChangeState(GameState.Error);
            return;
        }

        Debug.Log(result.Message);
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

        stateMachine.ChangeState(GameState.LoadingGame);
        sceneLoader.LoadGame();
    }

    public void ShutdownToMainMenu()
    {
        if (!HasRequiredReferences()) return;

        stateMachine.ChangeState(GameState.Disconnecting);

        connectionService.Shutdown();

        sceneLoader.LoadMainMenu();
        stateMachine.ChangeState(GameState.MainMenu);
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
}