using System.Collections;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkSessionFlowService : MonoBehaviour, INetworkSessionService
{
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private GameStateMachine stateMachine;
    [SerializeField] private NetworkConnectionService connectionService;
    [SerializeField] private ProjectSceneFlowService sceneFlowService;
    [SerializeField] private NetworkSessionDisconnectHandler disconnectHandler;
    [SerializeField] private NetworkSessionFailureHandler failureHandler;
    [SerializeField] private UiErrorManager errorManager;

    private Coroutine shutdownRoutine;

    private void Awake()
    {
        HasRequiredReferences();
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
            Fail(result);
            return;
        }

        disconnectHandler.StartListening();

        if (!sceneFlowService.LoadScene(ProjectSceneKind.Lobby))
        {
            Fail(ConnectionResult.Fail(
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
            Fail(result);
            return;
        }

        disconnectHandler.StartListening();

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

        disconnectHandler.StopListening();
        connectionService.Shutdown();

        yield return null;

        if (!sceneFlowService.LoadScene(ProjectSceneKind.MainMenu))
            Debug.LogError("Failed to load main menu after network shutdown.", this);

        shutdownRoutine = null;
    }

    private void Fail(ConnectionResult result)
    {
        disconnectHandler.StopListening();
        failureHandler.FailAndReturnToMainMenu(result);
    }

    private bool HasRequiredReferences()
    {
        bool valid = true;

        valid &= ValidateRequiredReference(networkManager, nameof(networkManager));
        valid &= ValidateRequiredReference(stateMachine, nameof(stateMachine));
        valid &= ValidateRequiredReference(connectionService, nameof(connectionService));
        valid &= ValidateRequiredReference(sceneFlowService, nameof(sceneFlowService));
        valid &= ValidateRequiredReference(disconnectHandler, nameof(disconnectHandler));
        valid &= ValidateRequiredReference(failureHandler, nameof(failureHandler));
        valid &= ValidateRequiredReference(errorManager, nameof(errorManager));

        return valid;
    }

    private bool ValidateRequiredReference(Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError($"{nameof(NetworkSessionFlowService)} is missing '{fieldName}'.", this);
        return false;
    }
}