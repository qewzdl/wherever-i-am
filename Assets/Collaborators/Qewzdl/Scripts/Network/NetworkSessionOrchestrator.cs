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
        DontDestroyOnLoad(gameObject);
    }

    public async Task HostLanAsync()
    {
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
        stateMachine.ChangeState(GameState.Connecting);

        ConnectionResult result = await connectionService.StartClientAsync(ip);

        if (!result.Success)
        {
            Debug.LogError(result.Message);
            stateMachine.ChangeState(GameState.Error);
            return;
        }
    }

    public void StartGame()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        stateMachine.ChangeState(GameState.LoadingGame);
        sceneLoader.LoadGame();
    }

    public void ShutdownToMainMenu()
    {
        stateMachine.ChangeState(GameState.Disconnecting);

        connectionService.Shutdown();

        sceneLoader.LoadMainMenu();
        stateMachine.ChangeState(GameState.MainMenu);
    }
}