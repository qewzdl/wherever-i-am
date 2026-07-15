using System;
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
    [SerializeField] private NetworkSessionShutdownCoordinator shutdownCoordinator;
    [SerializeField] private UiErrorManager errorManager;
    [SerializeField] private GameMapService gameMapService;

    private ProjectSceneFlowService subscribedSceneFlowService;
    private bool sceneFlowSubscribed;

    internal IServiceResolver SessionServices => shutdownCoordinator != null
        ? shutdownCoordinator.SessionServices
        : null;

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

        if (!TryBeginSession(NetworkSessionState.StartingHost, "Host LAN requested."))
            return;

        stateMachine.ChangeState(GameState.Connecting);
        disconnectHandler.StartListening();

        ConnectionResult result = await connectionService.StartHostAsync();

        if (!result.Success)
        {
            if (result.ErrorCode != ConnectionErrorCode.Cancelled)
                await FailAsync(result);

            return;
        }

        if (!connectionService.IsConnectionReady)
        {
            await FailAsync(CreateConnectionLostDuringStartupResult());
            return;
        }

        if (!connectionService.IsConnectionReady)
        {
            await FailAsync(CreateConnectionLostDuringStartupResult());
            return;
        }

        if (!sceneFlowService.LoadScene(ProjectSceneKind.Lobby))
        {
            await FailAsync(ConnectionResult.Fail(
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

        if (!TryBeginSession(NetworkSessionState.StartingClient, "Join LAN requested."))
            return;

        stateMachine.ChangeState(GameState.Connecting);
        disconnectHandler.StartListening();

        ConnectionResult result = await connectionService.StartClientAsync(ip);

        if (!result.Success)
        {
            if (result.ErrorCode != ConnectionErrorCode.Cancelled)
                await FailAsync(result);

            return;
        }

        if (!connectionService.IsConnectionReady)
        {
            await FailAsync(CreateConnectionLostDuringStartupResult());
            return;
        }

        if (!connectionService.IsConnectionReady)
        {
            await FailAsync(CreateConnectionLostDuringStartupResult());
            return;
        }

        RuntimeLog.Info(result.DebugMessage);
    }

    public void StartGame(int mapId)
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

        if (!gameMapService.SelectMap(mapId))
        {
            _ = FailAsync(ConnectionResult.Fail(
                ConnectionErrorCode.Unknown,
                "Failed to select the game map.",
                $"Invalid game map id: {mapId}.",
                true
            ));
            return;
        }

        if (!sceneFlowService.LoadScene(ProjectSceneKind.Game))
        {
            _ = FailAsync(ConnectionResult.Fail(
                ConnectionErrorCode.Unknown,
                "Failed to load the game.",
                "Failed to load game scene.",
                true
            ));
        }
    }

    public void ShutdownToMainMenu()
    {
        _ = ShutdownToMainMenuAsync();
    }

    public Task ShutdownToMainMenuAsync()
    {
        if (!HasRequiredReferences())
            return Task.CompletedTask;

        return shutdownCoordinator.ShutdownAndWaitAsync(NetworkShutdownMode.Graceful);
    }

    internal Task ShutdownAfterFailureAsync(ConnectionResult failure)
    {
        if (failure == null)
            throw new ArgumentNullException(nameof(failure));

        if (!HasRequiredReferences())
            return Task.CompletedTask;

        return shutdownCoordinator.ShutdownAndWaitAsync(failure);
    }

    internal bool ConfigureSessionScopeController(
        ServiceScope globalScope,
        IGameMapSessionService maps,
        IGameplayNoiseService gameplayNoise,
        SceneRuntimeScopeRegistry sceneScopes)
    {
        if (shutdownCoordinator == null)
            return false;

        return shutdownCoordinator.ConfigureSessionScopeController(
            globalScope,
            maps,
            gameplayNoise,
            sceneScopes);
    }

    internal bool TryGetSessionServiceScope(out ServiceScope scope)
    {
        scope = null;
        return shutdownCoordinator != null &&
               shutdownCoordinator.TryGetSessionServiceScope(out scope);
    }

    internal bool TryGetSessionServiceRegistry(out ISessionServiceRegistry registry)
    {
        registry = null;
        return shutdownCoordinator != null &&
               shutdownCoordinator.TryGetSessionServiceRegistry(out registry);
    }

    internal bool TryRegisterSessionServices(
        Action<ISessionServiceRegistrar> registerServices,
        out SessionServiceRegistration registrations,
        out Exception failure)
    {
        registrations = null;

        if (shutdownCoordinator != null)
        {
            return shutdownCoordinator.TryRegisterSessionServices(
                registerServices,
                out registrations,
                out failure);
        }

        failure = new InvalidOperationException(
            $"{nameof(NetworkSessionShutdownCoordinator)} is not configured.");

        return false;
    }

    internal bool TryOpenPlayerScope(
        ulong networkObjectId,
        ulong ownerClientId,
        bool isLocalPlayer,
        Action<IPlayerServiceRegistrar> registerReplicatedServices,
        Action<IPlayerServiceRegistrar> registerLocalServices,
        out PlayerScopeRegistration registration,
        out Exception failure)
    {
        registration = null;

        if (shutdownCoordinator != null)
        {
            return shutdownCoordinator.TryOpenPlayerScope(
                networkObjectId,
                ownerClientId,
                isLocalPlayer,
                registerReplicatedServices,
                registerLocalServices,
                out registration,
                out failure);
        }

        failure = new InvalidOperationException(
            $"{nameof(NetworkSessionShutdownCoordinator)} is not configured.");

        return false;
    }

    internal void DisposeSessionScopeController()
    {
        shutdownCoordinator?.DisposeSessionScopeController();
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

    private void HandleSceneLoadFailed(ProjectSceneKind scene)
    {
        NetworkSessionState state = sessionStateMachine.CurrentState;

        if (scene == ProjectSceneKind.Lobby &&
            (state == NetworkSessionState.StartingHost ||
             state == NetworkSessionState.StartingClient))
        {
            _ = FailAsync(ConnectionResult.Fail(
                ConnectionErrorCode.LobbySceneLoadFailed,
                "Failed to load the lobby.",
                "Lobby scene loading did not complete for all clients.",
                true));

            return;
        }

        if (scene != ProjectSceneKind.Game || state != NetworkSessionState.LoadingGame)
            return;

        _ = FailAsync(ConnectionResult.Fail(
            ConnectionErrorCode.Unknown,
            "Failed to load the selected map.",
            "Game map loading did not complete.",
            true
        ));
    }

    private Task FailAsync(ConnectionResult result)
    {
        return shutdownCoordinator.ShutdownAndWaitAsync(result);
    }

    private static ConnectionResult CreateConnectionLostDuringStartupResult()
    {
        return ConnectionResult.Fail(
            ConnectionErrorCode.ConnectionFailed,
            "Connection was interrupted while the session was starting.",
            "NetworkManager stopped between connection completion and session callback registration.",
            true);
    }

    private bool TryBeginSession(NetworkSessionState startingState, string reason)
    {
        if (!sessionStateMachine.CanStartConnection)
        {
            Debug.LogWarning(
                $"Cannot start a network session from state '{sessionStateMachine.CurrentState}'.",
                this);

            return false;
        }

        if (!shutdownCoordinator.TryOpenSessionScope())
            return false;

        if (sessionStateMachine.TryChangeState(startingState, reason))
            return true;

        _ = shutdownCoordinator.ShutdownAndWaitAsync(ConnectionResult.Fail(
            ConnectionErrorCode.Unknown,
            "Failed to start the network session.",
            $"Failed to enter session state '{startingState}'.",
            true));

        return false;
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
        subscribedSceneFlowService.SceneLoadFailed += HandleSceneLoadFailed;
        sceneFlowSubscribed = true;
    }

    private void UnsubscribeFromSceneFlowService()
    {
        if (!sceneFlowSubscribed)
            return;

        if (subscribedSceneFlowService != null)
        {
            subscribedSceneFlowService.SceneLoadCompleted -= HandleSceneLoadCompleted;
            subscribedSceneFlowService.SceneLoadFailed -= HandleSceneLoadFailed;
        }

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
        valid &= ValidateRequiredReference(shutdownCoordinator, nameof(shutdownCoordinator));
        valid &= ValidateRequiredReference(errorManager, nameof(errorManager));
        valid &= ValidateRequiredReference(gameMapService, nameof(gameMapService));

        return valid;
    }

    private bool ValidateRequiredReference(UnityEngine.Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError($"{nameof(NetworkSessionFlowService)} is missing '{fieldName}'.", this);
        return false;
    }
}
