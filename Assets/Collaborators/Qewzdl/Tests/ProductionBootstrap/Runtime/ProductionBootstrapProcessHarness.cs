using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

internal enum ProductionBootstrapRole
{
    None = 0,
    Host = 1,
    Client = 2,
    LateClient = 3
}

[Serializable]
internal sealed class ProductionBootstrapRoleResult
{
    public string role;
    public bool succeeded;
    public string message;
    public string exception;
    public string unityVersion;
    public float durationSeconds;
    public string[] phases;
}

[DefaultExecutionOrder(-2000)]
internal sealed class ProductionBootstrapProcessHarness : MonoBehaviour
{
    private const string RoleArgument = "-gBootstrapRole";
    private const string RunDirectoryArgument = "-gBootstrapRunDirectory";
    private const string TimeoutArgument = "-gBootstrapTimeoutSeconds";
    private const string StartGameSignal = "start-game.signal";
    private const string ShutdownSignal = "shutdown.signal";
    private const float DefaultStepTimeoutSeconds = 90f;

    private readonly List<string> completedPhases = new();

    private ProductionBootstrapRole role;
    private string roleName;
    private string runDirectory;
    private float stepTimeoutSeconds;
    private float startedAt;
    private ProjectContext context;
    private AppRuntime appRuntime;
    private NetworkSessionStateMachine sessionState;
    private NetworkManager networkManager;
    private INetworkSessionService session;
    private bool resultWritten;
    private bool startingHostObserved;
    private bool startingClientObserved;
    private bool loadingLobbyObserved;
    private bool lobbyObserved;
    private bool loadingGameObserved;
    private bool inGameObserved;
    private bool disconnectingObserved;
    private bool offlineObserved;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void TryCreate()
    {
        string[] arguments = Environment.GetCommandLineArgs();

        if (!TryReadRole(arguments, out ProductionBootstrapRole parsedRole) ||
            !TryReadArgument(arguments, RunDirectoryArgument, out string resultDirectory))
        {
            return;
        }

        GameObject root = new(nameof(ProductionBootstrapProcessHarness));
        DontDestroyOnLoad(root);

        ProductionBootstrapProcessHarness harness =
            root.AddComponent<ProductionBootstrapProcessHarness>();
        harness.role = parsedRole;
        harness.roleName = GetRoleName(parsedRole);
        harness.runDirectory = Path.GetFullPath(resultDirectory);
        harness.stepTimeoutSeconds = ReadTimeout(arguments);
    }

    private async void Start()
    {
        startedAt = Time.realtimeSinceStartup;
        Application.runInBackground = true;

        try
        {
            Directory.CreateDirectory(runDirectory);
            await BindProductionRuntimeAsync();
            SubscribeToSessionState();
            await WaitForMainMenuAsync();
            ReportPhase("main-menu");

            session = G.Resolve<INetworkSessionService>();

            if (role == ProductionBootstrapRole.Host)
                await RunHostAsync();
            else
                await RunClientAsync(role == ProductionBootstrapRole.LateClient);

            Complete(true, "Production bootstrap lifecycle completed.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            Complete(false, exception.Message, exception);
        }
    }

    private async Task BindProductionRuntimeAsync()
    {
        await WaitUntilAsync(
            TryBindProductionRuntime,
            "A single production ProjectContext/AppRuntime was not created from Bootstrap.unity.");
    }

    private bool TryBindProductionRuntime()
    {
        // The acceptance Player starts from the untouched production scene, so
        // it has no serialized test reference. Discover the bootstrap root once,
        // require it to be unique, then use only its production composition API.
        ProjectContext[] contexts = FindObjectsByType<ProjectContext>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        AppRuntime[] runtimes = FindObjectsByType<AppRuntime>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        if (contexts.Length != 1 || runtimes.Length != 1)
            return false;

        ProjectContext foundContext = contexts[0];
        AppRuntime foundRuntime = runtimes[0];

        if (!ReferenceEquals(
                foundRuntime.GetComponent<ProjectContext>(),
                foundContext))
        {
            return false;
        }

        NetworkSessionStateMachine foundSessionState =
            foundContext.GetComponent<NetworkSessionStateMachine>();

        if (foundSessionState == null || foundContext.NetworkManager == null)
            return false;

        context = foundContext;
        appRuntime = foundRuntime;
        sessionState = foundSessionState;
        networkManager = foundContext.NetworkManager;
        return true;
    }

    private void SubscribeToSessionState()
    {
        ObserveSessionState(sessionState.CurrentState);
        sessionState.StateChanged += HandleSessionStateChanged;
    }

    private void HandleSessionStateChanged(
        NetworkSessionState previous,
        NetworkSessionState current)
    {
        ObserveSessionState(current);
    }

    private void ObserveSessionState(NetworkSessionState state)
    {
        startingHostObserved |= state == NetworkSessionState.StartingHost;
        startingClientObserved |= state == NetworkSessionState.StartingClient;
        loadingLobbyObserved |= state == NetworkSessionState.LoadingLobby;
        lobbyObserved |= state == NetworkSessionState.Lobby;
        loadingGameObserved |= state == NetworkSessionState.LoadingGame;
        inGameObserved |= state == NetworkSessionState.InGame;
        disconnectingObserved |= state == NetworkSessionState.Disconnecting;
        offlineObserved |= state == NetworkSessionState.Offline;
    }

    private async Task RunHostAsync()
    {
        await session.HostLanAsync();
        ThrowIfRuntimeEnteredError("Host startup");

        await WaitUntilAsync(
            () => networkManager.IsHost &&
                  networkManager.IsListening &&
                  networkManager.IsConnectedClient,
            "Production HostLanAsync did not start a listening NGO host.");
        ReportPhase("network");

        await WaitForReadySceneAsync(ProjectSceneKind.Lobby);
        ReportPhase("lobby");

        await WaitUntilAsync(
            () => File.Exists(GetProtocolPath(StartGameSignal)) &&
                  networkManager.ConnectedClientsIds.Count >= 2,
            "Host did not receive the first ready client or the Game start signal.");

        session.StartGame(G.Resolve<IGameMapCatalog>().DefaultMapId);
        await WaitForReadySceneAsync(ProjectSceneKind.Game);
        ReportPhase("game");

        NetworkObjectiveFlow objectiveFlow = null;
        await WaitUntilAsync(
            () =>
            {
                objectiveFlow =
                    FindFirstObjectByType<NetworkObjectiveFlow>();
                return objectiveFlow != null &&
                       objectiveFlow.HasActiveObjective;
            },
            "Host objective flow did not activate its first objective.");

        ObjectiveDefinition activeObjective = objectiveFlow.ActiveObjective;

        if (!objectiveFlow.ReportObjectiveProgressServerOnly(
                activeObjective,
                0.5f,
                networkManager.LocalClientId) ||
            !Mathf.Approximately(
                objectiveFlow.CurrentObjective.Progress01,
                0.5f))
        {
            throw new InvalidOperationException(
                "Host could not commit server-authoritative objective progress.");
        }

        ReportPhase("objective-progress");

        await WaitUntilAsync(
            () => networkManager.ConnectedClientsIds.Count >= 3 &&
                  File.Exists(GetProtocolPath(
                      "client.objective-progress.ready")) &&
                  File.Exists(GetProtocolPath(
                      "late-client.objective-progress.ready")),
            "Host did not receive objective snapshots from both clients.");

        IServiceResolver services =
            context.SessionOrchestrator.SessionServices;

        if (services == null ||
            !services.TryResolve(
                out IMatchCompletionService matchService) ||
            matchService is not NetworkGameFlow gameFlow)
        {
            throw new InvalidOperationException(
                "Host could not resolve the production match service.");
        }

        int matchResolvedCount = 0;
        gameFlow.MatchResolved += _ => matchResolvedCount++;

        if (!objectiveFlow.CompleteObjectiveServerOnly(
                activeObjective,
                networkManager.LocalClientId))
        {
            throw new InvalidOperationException(
                "Host could not complete the active objective.");
        }

        await WaitUntilAsync(
            () => objectiveFlow.CurrentObjective.State ==
                      ObjectiveRuntimeState.Completed &&
                  gameFlow.CurrentResult.Source ==
                      MatchResultSource.Objective,
            "Objective completion did not commit the match result.");

        bool duplicateCompletionAccepted =
            objectiveFlow.CompleteObjectiveServerOnly(
                activeObjective,
                networkManager.LocalClientId);

        if (duplicateCompletionAccepted || matchResolvedCount != 1)
        {
            throw new InvalidOperationException(
                "Objective completion was accepted or raised MatchResolved more than once.");
        }

        ReportPhase("objective-complete");

        await WaitUntilAsync(
            () => File.Exists(GetProtocolPath(
                      "client.objective-complete.ready")) &&
                  File.Exists(GetProtocolPath(
                      "late-client.objective-complete.ready")),
            "Host did not receive objective completion from both clients.");
        await WaitUntilAsync(
            () => File.Exists(GetProtocolPath(ShutdownSignal)),
            "Host did not receive the shutdown signal.");

        NetworkShutdownResult shutdown = await session.ShutdownToMainMenuAsync();
        ValidateShutdownResult(shutdown, "host");

        await WaitForCleanMainMenuAsync();
        ReportPhase("shutdown");
        ValidateHostStateHistory();
    }

    private async Task RunClientAsync(bool lateClient)
    {
        await session.JoinLanAsync("127.0.0.1");
        ThrowIfRuntimeEnteredError("Client startup");

        await WaitUntilAsync(
            () => networkManager.IsClient &&
                  !networkManager.IsServer &&
                  networkManager.IsListening &&
                  networkManager.IsConnectedClient,
            "Production JoinLanAsync did not connect a dedicated NGO client.");
        ReportPhase("network");

        if (!lateClient)
        {
            await WaitForReadySceneAsync(ProjectSceneKind.Lobby);
            ReportPhase("lobby");
        }

        await WaitForReadySceneAsync(ProjectSceneKind.Game);
        ReportPhase("game");

        NetworkObjectiveFlow objectiveFlow = null;
        await WaitUntilAsync(
            () =>
            {
                objectiveFlow =
                    FindFirstObjectByType<NetworkObjectiveFlow>();
                return objectiveFlow != null &&
                       objectiveFlow.CurrentObjective.State ==
                           ObjectiveRuntimeState.Active &&
                       Mathf.Approximately(
                           objectiveFlow.CurrentObjective.Progress01,
                           0.5f);
            },
            lateClient
                ? "Late client did not receive the current objective snapshot."
                : "Client did not receive server-authoritative objective progress.");
        ReportPhase("objective-progress");

        await WaitUntilAsync(
            () =>
            {
                IServiceResolver services =
                    context.SessionOrchestrator.SessionServices;
                return objectiveFlow.CurrentObjective.State ==
                           ObjectiveRuntimeState.Completed &&
                       services != null &&
                       services.TryResolve(
                           out IMatchCompletionService matchService) &&
                       matchService is NetworkGameFlow gameFlow &&
                       gameFlow.CurrentResult.Source ==
                           MatchResultSource.Objective;
            },
            "Client did not synchronize objective match completion.");
        ReportPhase("objective-complete");

        await WaitUntilAsync(
            () => File.Exists(GetProtocolPath(ShutdownSignal)),
            "Client did not receive the shutdown signal.");
        NetworkShutdownResult shutdown = await session.ShutdownToMainMenuAsync();
        ValidateShutdownResult(
            shutdown,
            lateClient ? "late client" : "client");

        await WaitForCleanMainMenuAsync();
        ReportPhase("shutdown");
        ValidateClientStateHistory(lateClient);
    }

    private async Task WaitForMainMenuAsync()
    {
        await WaitUntilAsync(
            () => context != null &&
                  appRuntime != null &&
                  context.IsReady &&
                  G.IsReady &&
                  context.StateMachine != null &&
                  context.StateMachine.CurrentState == GameState.MainMenu &&
                  context.GetActiveSceneKind() == ProjectSceneKind.MainMenu &&
                  G.TryResolve(out IProjectSceneFlowService flow) &&
                  !flow.HasPendingOperation,
            "ProjectContext/AppRuntime did not publish G and commit MainMenu.");

        if (appRuntime.SceneScopeCount <= 0)
        {
            throw new InvalidOperationException(
                "AppRuntime reached MainMenu without an active scene scope.");
        }
    }

    private async Task WaitForReadySceneAsync(ProjectSceneKind sceneKind)
    {
        await WaitUntilAsync(
            () => IsReadyScene(sceneKind),
            $"Production runtime did not commit ready scene '{sceneKind}'.");

        IServiceResolver services = context.SessionOrchestrator.SessionServices;

        if (!SessionServiceReadinessPolicy.Validate(sceneKind, services, out string error))
            throw new InvalidOperationException(error);

        if (!SessionServiceReadinessPolicy.ValidateServerPhase(
                sceneKind,
                services,
                out string phaseError))
        {
            throw new InvalidOperationException(phaseError);
        }
    }

    private bool IsReadyScene(ProjectSceneKind sceneKind)
    {
        if (context == null ||
            context.StateMachine == null ||
            context.GetActiveSceneKind() != sceneKind)
        {
            return false;
        }

        GameState expectedState = sceneKind == ProjectSceneKind.Lobby
            ? GameState.Lobby
            : GameState.InGame;
        NetworkSessionState expectedSessionState =
            sceneKind == ProjectSceneKind.Lobby
                ? NetworkSessionState.Lobby
                : NetworkSessionState.InGame;

        if (context.StateMachine.CurrentState != expectedState ||
            sessionState.CurrentState != expectedSessionState ||
            context.SessionOrchestrator.SessionServices == null)
        {
            return false;
        }

        IServiceResolver services = context.SessionOrchestrator.SessionServices;
        return SessionServiceReadinessPolicy.Validate(sceneKind, services, out _) &&
               SessionServiceReadinessPolicy.ValidateServerPhase(
                   sceneKind,
                   services,
                   out _);
    }

    private async Task WaitForCleanMainMenuAsync()
    {
        await WaitUntilAsync(
            () => context != null &&
                  context.StateMachine != null &&
                  context.StateMachine.CurrentState == GameState.MainMenu &&
                  context.GetActiveSceneKind() == ProjectSceneKind.MainMenu &&
                  sessionState.CurrentState == NetworkSessionState.Offline &&
                  !networkManager.IsListening &&
                  !networkManager.IsClient &&
                  !networkManager.IsServer &&
                  context.SessionOrchestrator.SessionServices == null &&
                  G.TryResolve(out IProjectSceneFlowService flow) &&
                  !flow.HasPendingOperation,
            "Process did not finish NGO and scope cleanup before MainMenu.",
            false);

        if (!G.IsReady || !context.IsReady || appRuntime.SceneScopeCount <= 0)
        {
            throw new InvalidOperationException(
                "Global runtime or MainMenu scene scope was lost during Session shutdown.");
        }
    }

    private void ValidateHostStateHistory()
    {
        if (!startingHostObserved ||
            !loadingLobbyObserved ||
            !lobbyObserved ||
            !loadingGameObserved ||
            !inGameObserved ||
            !disconnectingObserved ||
            !offlineObserved)
        {
            throw new InvalidOperationException(
                "Host did not observe the complete " +
                "StartingHost -> LoadingLobby -> Lobby -> LoadingGame -> InGame -> " +
                "Disconnecting -> Offline state history.");
        }
    }

    private void ValidateClientStateHistory(bool lateClient)
    {
        if (!startingClientObserved ||
            !loadingGameObserved ||
            !inGameObserved ||
            !disconnectingObserved ||
            !offlineObserved)
        {
            throw new InvalidOperationException(
                "Client did not observe the required " +
                "StartingClient -> LoadingGame -> InGame -> " +
                "Disconnecting -> Offline states.");
        }

        if (!lateClient && !lobbyObserved)
        {
            throw new InvalidOperationException(
                "The first client did not commit Lobby before Game.");
        }

        if (!lateClient && !loadingLobbyObserved)
        {
            throw new InvalidOperationException(
                "The first client did not observe LoadingLobby before Lobby.");
        }

        if (lateClient && lobbyObserved)
        {
            throw new InvalidOperationException(
                "The late client entered Lobby instead of synchronizing directly to Game.");
        }
    }

    private static void ValidateShutdownResult(
        NetworkShutdownResult shutdown,
        string owner)
    {
        if (shutdown.Succeeded &&
            shutdown.NetworkStopped &&
            shutdown.SessionScopeClosed &&
            shutdown.MainMenuReady)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Coordinated {owner} shutdown failed. {shutdown.Message}");
    }

    private void ThrowIfRuntimeEnteredError(string operation)
    {
        if (context != null &&
            context.StateMachine != null &&
            context.StateMachine.CurrentState == GameState.Error)
        {
            throw new InvalidOperationException(
                $"{operation} moved the production runtime to Error.");
        }
    }

    private async Task WaitUntilAsync(
        Func<bool> predicate,
        string failureMessage,
        bool failOnGameError = true)
    {
        float deadline = Time.realtimeSinceStartup + stepTimeoutSeconds;

        while (!predicate.Invoke())
        {
            if (failOnGameError &&
                context != null &&
                context.StateMachine != null &&
                context.StateMachine.CurrentState == GameState.Error)
            {
                throw new InvalidOperationException(
                    $"{failureMessage} Runtime entered GameState.Error.");
            }

            if (Time.realtimeSinceStartup >= deadline)
                throw new TimeoutException(failureMessage);

            await Task.Delay(25);
        }
    }

    private void ReportPhase(string phase)
    {
        completedPhases.Add(phase);
        WriteAtomic(
            GetProtocolPath($"{roleName}.{phase}.ready"),
            DateTime.UtcNow.ToString("O"));
    }

    private void Complete(
        bool succeeded,
        string message,
        Exception exception = null)
    {
        if (resultWritten)
            return;

        resultWritten = true;

        if (sessionState != null)
            sessionState.StateChanged -= HandleSessionStateChanged;

        ProductionBootstrapRoleResult result = new()
        {
            role = roleName,
            succeeded = succeeded,
            message = message ?? string.Empty,
            exception = exception?.ToString() ?? string.Empty,
            unityVersion = Application.unityVersion,
            durationSeconds = Time.realtimeSinceStartup - startedAt,
            phases = completedPhases.ToArray()
        };

        try
        {
            WriteAtomic(
                GetProtocolPath($"{roleName}.result.json"),
                JsonUtility.ToJson(result, true));
        }
        catch (Exception writeException)
        {
            Debug.LogException(writeException, this);
            succeeded = false;
        }

        Application.Quit(succeeded ? 0 : 1);
    }

    private string GetProtocolPath(string fileName)
    {
        return Path.Combine(runDirectory, fileName);
    }

    private static void WriteAtomic(string path, string content)
    {
        string temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, content);

        if (File.Exists(path))
            File.Delete(path);

        File.Move(temporaryPath, path);
    }

    private static float ReadTimeout(string[] arguments)
    {
        if (TryReadArgument(arguments, TimeoutArgument, out string value) &&
            float.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float timeout) &&
            timeout >= 10f)
        {
            return timeout;
        }

        return DefaultStepTimeoutSeconds;
    }

    private static bool TryReadRole(
        string[] arguments,
        out ProductionBootstrapRole parsedRole)
    {
        parsedRole = ProductionBootstrapRole.None;

        if (!TryReadArgument(arguments, RoleArgument, out string value))
            return false;

        if (string.Equals(value, "host", StringComparison.OrdinalIgnoreCase))
            parsedRole = ProductionBootstrapRole.Host;
        else if (string.Equals(value, "client", StringComparison.OrdinalIgnoreCase))
            parsedRole = ProductionBootstrapRole.Client;
        else if (string.Equals(value, "late-client", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(value, "lateclient", StringComparison.OrdinalIgnoreCase))
            parsedRole = ProductionBootstrapRole.LateClient;

        return parsedRole != ProductionBootstrapRole.None;
    }

    private static string GetRoleName(ProductionBootstrapRole parsedRole)
    {
        return parsedRole switch
        {
            ProductionBootstrapRole.Host => "host",
            ProductionBootstrapRole.Client => "client",
            ProductionBootstrapRole.LateClient => "late-client",
            _ => "unknown"
        };
    }

    private static bool TryReadArgument(
        string[] arguments,
        string name,
        out string value)
    {
        for (int i = 0; i < arguments.Length - 1; i++)
        {
            if (!string.Equals(
                    arguments[i],
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = arguments[i + 1];
            return !string.IsNullOrWhiteSpace(value);
        }

        value = string.Empty;
        return false;
    }
}
