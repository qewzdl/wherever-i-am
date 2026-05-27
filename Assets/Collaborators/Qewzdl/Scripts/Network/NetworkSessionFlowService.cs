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
    [SerializeField] private NetworkSessionStateMachine sessionStateMachine;
    [SerializeField] private NetworkConnectionService connectionService;
    [SerializeField] private ProjectSceneFlowService sceneFlowService;
    [SerializeField] private NetworkSessionDisconnectHandler disconnectHandler;
    [SerializeField] private NetworkSessionFailureHandler failureHandler;
    [SerializeField] private UiErrorManager errorManager;

    private Coroutine shutdownRoutine;

    private ProjectSceneFlowService subscribedSceneFlowService;
    private bool sceneFlowSubscribed;

    private void Awake()
    {
        HasRequiredReferences();
    }

    private void OnEnable()
    {
        SubscribeToSceneFlowService();
    }

    private void OnDisable()
    {
        UnsubscribeFromSceneFlowService();
    }

    public async Task HostLanAsync()
    {
        if (!HasRequiredReferences())
            return;

        errorManager.HideError();

        if (connectionService.IsListening)
        {
            Debug.LogWarning("Network is already running.", this);
            return;
        }

        if (!sessionStateMachine.TryChangeState(NetworkSessionState.StartingHost, "Host LAN requested."))
            return;

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

        if (connectionService.IsListening)
        {
            Debug.LogWarning("Network is already running.", this);
            return;
        }

        if (!sessionStateMachine.TryChangeState(NetworkSessionState.StartingClient, "Join LAN requested."))
            return;

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
            Debug.LogWarning("Only server can start the game.", this);
            return;
        }

        if (!sessionStateMachine.TryChangeState(NetworkSessionState.LoadingGame, "Server requested game start."))
            return;

        if (!sceneFlowService.LoadScene(ProjectSceneKind.Game))
        {
            Fail(ConnectionResult.Fail(
                ConnectionErrorCode.Unknown,
                "Failed to load the game.",
                "Failed to load game scene.",
                true
            ));
        }
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
        if (!sessionStateMachine.TryChangeState(NetworkSessionState.Disconnecting, "Shutdown to main menu requested."))
        {
            shutdownRoutine = null;
            yield break;
        }

        stateMachine.ChangeState(GameState.Disconnecting);

        disconnectHandler.StopListening();
        connectionService.Shutdown();

        yield return null;

        if (!sceneFlowService.LoadScene(ProjectSceneKind.MainMenu))
        {
            sessionStateMachine.TryChangeState(NetworkSessionState.Failed, "Failed to load main menu after network shutdown.");
            Debug.LogError("Failed to load main menu after network shutdown.", this);
        }

        shutdownRoutine = null;
    }

    private void HandleSceneLoadCompleted(ProjectSceneKind scene)
    {
        if (!HasRequiredReferences())
            return;

        switch (scene)
        {
            case ProjectSceneKind.Lobby:
                if (sessionStateMachine.CurrentState == NetworkSessionState.StartingHost ||
                    sessionStateMachine.CurrentState == NetworkSessionState.StartingClient)
                {
                    sessionStateMachine.TryChangeState(NetworkSessionState.Lobby, "Lobby scene synchronized.");
                }
                break;

            case ProjectSceneKind.Game:
                if (sessionStateMachine.CurrentState == NetworkSessionState.LoadingGame)
                    sessionStateMachine.TryChangeState(NetworkSessionState.InGame, "Game scene synchronized.");
                break;

            case ProjectSceneKind.MainMenu:
                if (sessionStateMachine.CurrentState == NetworkSessionState.Disconnecting)
                    sessionStateMachine.TryChangeState(NetworkSessionState.Offline, "Returned to main menu.");
                break;
        }
    }

    private void Fail(ConnectionResult result)
    {
        disconnectHandler.StopListening();

        if (sessionStateMachine.CurrentState != NetworkSessionState.Failed)
            sessionStateMachine.TryChangeState(NetworkSessionState.Failed, result.DebugMessage);

        failureHandler.FailAndReturnToMainMenu(result);
    }

    private void SubscribeToSceneFlowService()
    {
        if (sceneFlowSubscribed && subscribedSceneFlowService == sceneFlowService)
            return;

        UnsubscribeFromSceneFlowService();

        if (sceneFlowService == null)
            return;

        subscribedSceneFlowService = sceneFlowService;
        subscribedSceneFlowService.SceneLoadCompleted += HandleSceneLoadCompleted;
        sceneFlowSubscribed = true;
    }

    private void UnsubscribeFromSceneFlowService()
    {
        if (!sceneFlowSubscribed)
            return;

        if (subscribedSceneFlowService != null)
            subscribedSceneFlowService.SceneLoadCompleted -= HandleSceneLoadCompleted;

        subscribedSceneFlowService = null;
        sceneFlowSubscribed = false;
    }

    private bool HasRequiredReferences()
    {
        bool valid = true;

        valid &= ValidateRequiredReference(networkManager, nameof(networkManager));
        valid &= ValidateRequiredReference(stateMachine, nameof(stateMachine));
        valid &= ValidateRequiredReference(sessionStateMachine, nameof(sessionStateMachine));
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