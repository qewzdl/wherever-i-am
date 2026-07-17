using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

internal sealed class ShutdownReplicatedPlayerService : IReplicatedPlayerStateService
{
    public bool IsCrouching => false;
}

internal sealed class ShutdownLocalPlayerService : ILocalPlayerPresentationService
{
    public bool IsPresentationActive => true;
}

internal sealed class FailingPostLoadActionHandler :
    MonoBehaviour,
    IProjectSceneFlowServerActionHandler
{
    private ProjectSceneServerAction expectedAction;
    private ProjectSceneKind expectedScene;
    private IProjectSceneFlowServerActionHandler innerHandler;

    internal int ExecuteCount { get; private set; }
    internal int RollbackCount { get; private set; }

    internal void Configure(
        ProjectSceneServerAction action,
        ProjectSceneKind sceneKind,
        IProjectSceneFlowServerActionHandler inner = null)
    {
        expectedAction = action;
        expectedScene = sceneKind;
        innerHandler = inner;
    }

    public bool CanHandle(ProjectSceneServerAction action)
    {
        return action == expectedAction;
    }

    public ProjectSceneActionResult Validate(
        ProjectSceneServerAction action,
        ProjectSceneKind loadedScene)
    {
        if (!CanHandle(action) || loadedScene != expectedScene)
            return ProjectSceneActionResult.Failure("Unexpected test action or scene.");

        return innerHandler != null
            ? innerHandler.Validate(action, loadedScene)
            : ProjectSceneActionResult.Success();
    }

    public ProjectSceneActionResult Execute(
        ProjectSceneServerAction action,
        ProjectSceneKind loadedScene)
    {
        ExecuteCount++;
        ProjectSceneActionResult innerResult = innerHandler?.Execute(
            action,
            loadedScene);

        if (innerResult != null && !innerResult.Succeeded)
            return innerResult;

        return ProjectSceneActionResult.Failure(
            "Injected post-load action failure.",
            rollback: () =>
            {
                RollbackCount++;
                innerResult?.Rollback();
            });
    }
}

public sealed class NetworkSessionShutdownPlayModeTests
{
    private const string BootstrapScenePath =
        "Assets/Collaborators/Qewzdl/Scenes/Bootstrap.unity";
    private const float OperationTimeoutSeconds = 20f;
    private const ulong TestPlayerNetworkObjectId = ulong.MaxValue - 100;

    private Scene persistentScene;
    private GameObject persistentSceneProbe;
    private ProjectContext runtimeContext;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        persistentSceneProbe = new GameObject("NGO Shutdown PlayMode Test Probe");
        UnityEngine.Object.DontDestroyOnLoad(persistentSceneProbe);
        persistentScene = persistentSceneProbe.scene;

        yield return StopAndDestroyProjectRuntimeRoots();

        G.ResetRuntimeState();
        runtimeContext = null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        yield return StopAndDestroyProjectRuntimeRoots();

        G.ResetRuntimeState();
        runtimeContext = null;

        if (persistentSceneProbe != null)
            UnityEngine.Object.Destroy(persistentSceneProbe);

        yield return null;
    }

    [UnityTest]
    public IEnumerator HostSessionShutdown_CancelsOperationsAndCleansBeforeMainMenu()
    {
        yield return StartBootstrapAndWaitUntilReady();

        AppRuntime appRuntime = GetSinglePersistentComponent<AppRuntime>();
        NetworkSessionShutdownCoordinator shutdownCoordinator =
            GetSinglePersistentComponent<NetworkSessionShutdownCoordinator>();
        NetworkSessionStateMachine sessionStateMachine =
            GetSinglePersistentComponent<NetworkSessionStateMachine>();
        NetworkManager networkManager = runtimeContext.NetworkManager;
        IProjectSceneFlowService sceneFlow = G.Resolve<IProjectSceneFlowService>();
        bool lobbyCommittedWithoutReadyServices = false;

        runtimeContext.StateMachine.StateChanged += (previous, current) =>
        {
            if (current != GameState.Lobby)
                return;

            IServiceResolver services =
                runtimeContext.SessionOrchestrator.SessionServices;
            lobbyCommittedWithoutReadyServices =
                !SessionServiceReadinessPolicy.Validate(
                    ProjectSceneKind.Lobby,
                    services,
                    out string _) ||
                !SessionServiceReadinessPolicy.ValidateServerPhase(
                    ProjectSceneKind.Lobby,
                    services,
                    out string _);
        };

        Task hostStart = runtimeContext.SessionOrchestrator.HostLanAsync();
        yield return WaitForTask(hostStart, "Host startup did not complete.");
        yield return WaitForCondition(
            () => sessionStateMachine.CurrentState == NetworkSessionState.Lobby &&
                  runtimeContext.GetActiveSceneKind() == ProjectSceneKind.Lobby &&
                  !sceneFlow.HasPendingOperation,
            "Host did not reach the ready Lobby state.");

        Assert.That(
            lobbyCommittedWithoutReadyServices,
            Is.False,
            "Lobby state was committed before dynamic Session services were ready.");

        IServiceResolver sessionServices =
            runtimeContext.SessionOrchestrator.SessionServices;
        Assert.That(sessionServices, Is.Not.Null);
        Assert.That(sessionServices.IsDisposed, Is.False);
        Assert.That(sessionServices.Resolve<IChatReadService>(), Is.TypeOf<NetworkChatSession>());
        Assert.That(sessionServices.Resolve<IChatCommandService>(), Is.TypeOf<NetworkChatSession>());
        Assert.That(
            sessionServices.Resolve<ISessionPhaseService>(),
            Is.TypeOf<NetworkSessionPhaseService>());

        Assert.That(
            runtimeContext.SessionOrchestrator.TryOpenPlayerScope(
                TestPlayerNetworkObjectId,
                networkManager.LocalClientId,
                true,
                registrar => registrar.Register<IReplicatedPlayerStateService>(
                    new ShutdownReplicatedPlayerService()),
                registrar => registrar.Register<ILocalPlayerPresentationService>(
                    new ShutdownLocalPlayerService()),
                out PlayerScopeRegistration playerRegistration,
                out Exception playerScopeFailure),
            Is.True,
            playerScopeFailure?.ToString());

        IPlayerScopeRegistry playerRegistry =
            sessionServices.Resolve<IPlayerScopeRegistry>();
        Assert.That(
            playerRegistry.TryGetPlayerScope(
                TestPlayerNetworkObjectId,
                out IPlayerScope playerScope),
            Is.True);
        IServiceResolver playerServices = playerScope.Services;
        IServiceResolver localPlayerServices = playerScope.LocalServices;
        Assert.That(playerServices.Resolve<IReplicatedPlayerStateService>(), Is.Not.Null);
        Assert.That(localPlayerServices.Resolve<ILocalPlayerPresentationService>(), Is.Not.Null);

        GameMapService mapService =
            (GameMapService)sessionServices.Resolve<IGameMapSessionService>();
        int mapId = G.Resolve<IGameMapCatalog>().DefaultMapId;
        runtimeContext.SessionOrchestrator.StartGame(mapId);

        Assert.That(sceneFlow.HasPendingOperation, Is.True);
        yield return WaitForCondition(
            () => mapService.HasPendingOperation,
            "Game map operation did not become pending.");
        yield return WaitForCondition(
            () => sessionServices.TryResolve(out IMatchCompletionService _),
            "NetworkGameFlow did not publish its Session service.");
        Assert.That(
            sessionServices.Resolve<IMatchCompletionService>(),
            Is.TypeOf<NetworkGameFlow>());
        Assert.That(appRuntime.SceneScopeCount, Is.GreaterThan(0));

        List<string> lifecycle = new();
        int clientStoppedCount = 0;
        int serverStoppedCount = 0;
        int playerClosingCount = 0;
        int sessionStoppedCount = 0;
        bool callbacksObservedCanceledOperations = true;
        bool callbacksObservedLiveScopes = true;
        bool callbacksObservedUnregisteredMatchService = true;
        bool sessionStoppedAfterCallbacks = false;
        bool sessionStoppedAfterCleanup = false;
        bool mainMenuAfterCleanup = false;

        networkManager.OnClientStopped += wasHost =>
        {
            lifecycle.Add("OnClientStopped");
            clientStoppedCount++;
            callbacksObservedCanceledOperations &=
                !sceneFlow.HasPendingOperation && !mapService.HasPendingOperation;
            callbacksObservedLiveScopes &=
                wasHost &&
                !sessionServices.IsDisposed &&
                !playerScope.IsDisposed &&
                appRuntime.SceneScopeCount > 0;
            callbacksObservedUnregisteredMatchService &=
                !sessionServices.TryResolve(out IMatchCompletionService _);
        };
        networkManager.OnServerStopped += wasHost =>
        {
            lifecycle.Add("OnServerStopped");
            serverStoppedCount++;
            callbacksObservedCanceledOperations &=
                !sceneFlow.HasPendingOperation && !mapService.HasPendingOperation;
            callbacksObservedLiveScopes &=
                wasHost &&
                !sessionServices.IsDisposed &&
                !playerScope.IsDisposed &&
                appRuntime.SceneScopeCount > 0;
            callbacksObservedUnregisteredMatchService &=
                !sessionServices.TryResolve(out IMatchCompletionService _);
        };
        playerRegistry.PlayerScopeClosing += closingScope =>
        {
            if (closingScope.NetworkObjectId != TestPlayerNetworkObjectId)
                return;

            lifecycle.Add("PlayerScopeClosing");
            playerClosingCount++;
        };
        shutdownCoordinator.SessionStopped += () =>
        {
            lifecycle.Add("SessionStopped");
            sessionStoppedCount++;
            sessionStoppedAfterCallbacks =
                clientStoppedCount == 1 && serverStoppedCount == 1;
            sessionStoppedAfterCleanup =
                sessionServices.IsDisposed &&
                playerScope.IsDisposed &&
                playerRegistry.IsDisposed &&
                appRuntime.SceneScopeCount == 0 &&
                playerClosingCount == 1 &&
                !sceneFlow.HasPendingOperation &&
                !mapService.HasPendingOperation;
        };
        runtimeContext.StateMachine.StateChanged += (_, current) =>
        {
            if (current != GameState.MainMenu)
                return;

            lifecycle.Add("MainMenu");
            mainMenuAfterCleanup =
                sessionStoppedCount == 1 &&
                sessionServices.IsDisposed &&
                playerScope.IsDisposed &&
                playerRegistry.IsDisposed;
        };

        Task<NetworkShutdownResult> shutdown =
            shutdownCoordinator.ShutdownAndWaitAsync();
        Task<NetworkShutdownResult> repeatedShutdown =
            shutdownCoordinator.ShutdownAndWaitAsync();

        Assert.That(repeatedShutdown, Is.SameAs(shutdown));
        Assert.That(sceneFlow.HasPendingOperation, Is.False);
        Assert.That(mapService.HasPendingOperation, Is.False);

        yield return WaitForTask(shutdown, "Coordinated host shutdown did not complete.");

        NetworkShutdownResult shutdownResult = shutdown.Result;
        Assert.That(shutdownResult.Succeeded, Is.True, shutdownResult.Message);
        Assert.That(shutdownResult.NetworkStopped, Is.True);
        Assert.That(shutdownResult.SessionScopeClosed, Is.True);
        Assert.That(shutdownResult.MainMenuReady, Is.True);
        Assert.That(runtimeContext.StateMachine.CurrentState, Is.EqualTo(GameState.MainMenu));
        Assert.That(runtimeContext.GetActiveSceneKind(), Is.EqualTo(ProjectSceneKind.MainMenu));

        Assert.That(clientStoppedCount, Is.EqualTo(1));
        Assert.That(serverStoppedCount, Is.EqualTo(1));
        Assert.That(callbacksObservedCanceledOperations, Is.True);
        Assert.That(callbacksObservedLiveScopes, Is.True);
        Assert.That(callbacksObservedUnregisteredMatchService, Is.True);
        Assert.That(playerClosingCount, Is.EqualTo(1));
        Assert.That(sessionStoppedCount, Is.EqualTo(1));
        Assert.That(sessionStoppedAfterCallbacks, Is.True);
        Assert.That(sessionStoppedAfterCleanup, Is.True);
        Assert.That(mainMenuAfterCleanup, Is.True);
        Assert.That(networkManager.IsListening, Is.False);
        Assert.That(networkManager.IsClient, Is.False);
        Assert.That(networkManager.IsServer, Is.False);
        Assert.That(networkManager.ShutdownInProgress, Is.False);
        Assert.That(appRuntime.SceneScopeCount, Is.EqualTo(1));
        Assert.That(playerServices.IsDisposed, Is.True);
        Assert.That(localPlayerServices.IsDisposed, Is.True);
        Assert.That(
            lifecycle.IndexOf("SessionStopped"),
            Is.GreaterThan(lifecycle.IndexOf("OnClientStopped")));
        Assert.That(
            lifecycle.IndexOf("SessionStopped"),
            Is.GreaterThan(lifecycle.IndexOf("OnServerStopped")));
        Assert.That(
            lifecycle.IndexOf("MainMenu"),
            Is.GreaterThan(lifecycle.IndexOf("SessionStopped")));

        Task afterStop = shutdownCoordinator.ShutdownAndWaitAsync(
            NetworkShutdownMode.Immediate);
        Assert.That(afterStop.IsCompletedSuccessfully, Is.True);
        yield return null;
        Assert.That(sessionStoppedCount, Is.EqualTo(1));

        playerRegistration.Dispose();
    }

    [UnityTest]
    public IEnumerator ApplicationQuitForceAbort_ClosesRuntimeExactlyOnce()
    {
        yield return StartBootstrapAndWaitUntilReady();

        NetworkManager networkManager = runtimeContext.NetworkManager;
        NetworkSessionStateMachine sessionStateMachine =
            GetSinglePersistentComponent<NetworkSessionStateMachine>();
        AppRuntime appRuntime = GetSinglePersistentComponent<AppRuntime>();

        Task hostStart = runtimeContext.SessionOrchestrator.HostLanAsync();
        yield return WaitForTask(hostStart, "Host startup failed before force-abort test.");
        yield return WaitForCondition(
            () => sessionStateMachine.CurrentState == NetworkSessionState.Lobby &&
                  runtimeContext.GetActiveSceneKind() == ProjectSceneKind.Lobby &&
                  runtimeContext.SessionOrchestrator.SessionServices != null,
            "Host did not reach Lobby before force abort.");

        IServiceResolver sessionServices =
            runtimeContext.SessionOrchestrator.SessionServices;

        Assert.That(G.IsReady, Is.True);
        Assert.That(sessionServices.IsDisposed, Is.False);
        Assert.That(networkManager.IsListening, Is.True);

        runtimeContext.ForceAbortRuntimeForApplicationQuit();

        Assert.That(
            runtimeContext.LifecycleState,
            Is.EqualTo(ProjectRuntimeLifecycleState.Disposed));
        Assert.That(G.IsReady, Is.False);
        Assert.That(sessionServices.IsDisposed, Is.True);
        Assert.That(appRuntime.SceneScopeCount, Is.Zero);

        yield return WaitForCondition(
            () => !networkManager.IsListening &&
                  !networkManager.IsClient &&
                  !networkManager.IsServer,
            "NGO did not stop after application-quit force abort.");

        Assert.DoesNotThrow(runtimeContext.ForceAbortRuntimeForApplicationQuit);
        Assert.That(
            runtimeContext.LifecycleState,
            Is.EqualTo(ProjectRuntimeLifecycleState.Disposed));
        Assert.That(G.IsReady, Is.False);

        yield return null;
    }

    [UnityTest]
    public IEnumerator ShutdownResult_ReportsMainMenuFailureAndKeepsTaskHonest()
    {
        yield return StartBootstrapAndWaitUntilReady();

        NetworkSessionShutdownCoordinator shutdownCoordinator =
            GetSinglePersistentComponent<NetworkSessionShutdownCoordinator>();
        NetworkSessionStateMachine sessionStateMachine =
            GetSinglePersistentComponent<NetworkSessionStateMachine>();
        ProjectSceneFlowService sceneFlow = runtimeContext.SceneFlowService;
        bool previousIgnoreState = LogAssert.ignoreFailingMessages;

        Task hostStart = runtimeContext.SessionOrchestrator.HostLanAsync();
        yield return WaitForTask(hostStart, "Host startup did not complete.");
        yield return WaitForCondition(
            () => sessionStateMachine.CurrentState == NetworkSessionState.Lobby &&
                  runtimeContext.GetActiveSceneKind() == ProjectSceneKind.Lobby,
            "Host did not reach Lobby before the injected MainMenu failure.");

        Assert.Throws<InvalidOperationException>(() => runtimeContext.DisposeRuntime());

        try
        {
            LogAssert.ignoreFailingMessages = true;
            sceneFlow.enabled = false;

            Task<NetworkShutdownResult> shutdown =
                shutdownCoordinator.ShutdownAndWaitAsync();
            yield return WaitForTask(
                shutdown,
                "Shutdown result did not complete after MainMenu load rejection.");

            NetworkShutdownResult result = shutdown.Result;
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.NetworkStopped, Is.True);
            Assert.That(result.SessionScopeClosed, Is.True);
            Assert.That(result.MainMenuReady, Is.False);
            Assert.That(result.Exception, Is.Not.Null);
            Assert.That(result.Message, Does.Contain("main menu").IgnoreCase);
            Assert.That(runtimeContext.StateMachine.CurrentState, Is.EqualTo(GameState.Error));
        }
        finally
        {
            sceneFlow.enabled = true;
            LogAssert.ignoreFailingMessages = previousIgnoreState;
        }
    }

    [UnityTest]
    public IEnumerator ImmediateShutdown_IgnoresLateLobbySceneCompletion()
    {
        yield return StartBootstrapAndWaitUntilReady();

        NetworkSessionShutdownCoordinator shutdownCoordinator =
            GetSinglePersistentComponent<NetworkSessionShutdownCoordinator>();
        NetworkManager networkManager = runtimeContext.NetworkManager;
        IProjectSceneFlowService sceneFlow = G.Resolve<IProjectSceneFlowService>();
        bool enteredErrorAfterShutdownStarted = false;
        bool shutdownStarted = false;

        runtimeContext.StateMachine.StateChanged += (_, current) =>
        {
            if (shutdownStarted && current == GameState.Error)
                enteredErrorAfterShutdownStarted = true;
        };

        Task hostStart = runtimeContext.SessionOrchestrator.HostLanAsync();
        yield return WaitForCondition(
            () => sceneFlow.HasPendingOperation,
            "Host did not start the Lobby scene operation.");

        shutdownStarted = true;
        Task<NetworkShutdownResult> shutdown =
            shutdownCoordinator.ShutdownAndWaitAsync(
                NetworkShutdownMode.Immediate);

        yield return WaitForTask(hostStart, "Host startup task did not finish.");
        yield return WaitForTask(
            shutdown,
            "Immediate shutdown did not survive late Lobby completion.");

        NetworkShutdownResult result = shutdown.Result;
        Assert.That(result.Succeeded, Is.True, result.Message);
        Assert.That(result.NetworkStopped, Is.True);
        Assert.That(result.SessionScopeClosed, Is.True);
        Assert.That(result.MainMenuReady, Is.True);
        Assert.That(enteredErrorAfterShutdownStarted, Is.False);
        Assert.That(networkManager.IsListening, Is.False);
        Assert.That(runtimeContext.GetActiveSceneKind(), Is.EqualTo(ProjectSceneKind.MainMenu));
        Assert.That(runtimeContext.StateMachine.CurrentState, Is.EqualTo(GameState.MainMenu));
        Assert.That(sceneFlow.HasPendingOperation, Is.False);
    }

    [UnityTest]
    public IEnumerator LobbyChatContractLoss_EntersErrorAndCoordinatesShutdown()
    {
        yield return StartBootstrapAndWaitUntilReady();

        NetworkSessionShutdownCoordinator shutdownCoordinator =
            GetSinglePersistentComponent<NetworkSessionShutdownCoordinator>();
        NetworkSessionStateMachine sessionStateMachine =
            GetSinglePersistentComponent<NetworkSessionStateMachine>();
        NetworkManager networkManager = runtimeContext.NetworkManager;
        IProjectSceneFlowService sceneFlow = G.Resolve<IProjectSceneFlowService>();

        Task hostStart = runtimeContext.SessionOrchestrator.HostLanAsync();
        yield return WaitForTask(hostStart, "Host startup did not complete.");
        yield return WaitForCondition(
            () => sessionStateMachine.CurrentState == NetworkSessionState.Lobby &&
                  runtimeContext.GetActiveSceneKind() == ProjectSceneKind.Lobby &&
                  !sceneFlow.HasPendingOperation,
            "Host did not reach Lobby before the readiness-loss test.");

        IServiceResolver sessionServices =
            runtimeContext.SessionOrchestrator.SessionServices;
        NetworkChatSession chatSession =
            sessionServices.Resolve<IChatReadService>() as NetworkChatSession;
        bool errorObserved = false;
        bool failedSessionStateObserved = false;
        int sessionStoppedCount = 0;

        Assert.That(chatSession, Is.Not.Null);
        Assert.That(chatSession.NetworkObject.IsSpawned, Is.True);

        runtimeContext.StateMachine.StateChanged += (_, current) =>
            errorObserved |= current == GameState.Error;
        sessionStateMachine.StateChanged += (_, current) =>
            failedSessionStateObserved |= current == NetworkSessionState.Failed;
        shutdownCoordinator.SessionStopped += () => sessionStoppedCount++;

        LogAssert.Expect(
            LogType.Warning,
            new Regex(
                "SessionServiceReadinessPolicy: Game state 'Lobby' missing " +
                "required dynamic Session contract\\(s\\): .*IChatReadService.*" +
                "IChatCommandService"));

        chatSession.NetworkObject.Despawn(true);

        yield return WaitForCondition(
            () => runtimeContext.StateMachine.CurrentState == GameState.MainMenu &&
                  runtimeContext.GetActiveSceneKind() == ProjectSceneKind.MainMenu &&
                  !networkManager.IsListening &&
                  !shutdownCoordinator.IsShutdownInProgress,
            "Chat readiness loss did not complete coordinated shutdown.");

        Assert.That(errorObserved, Is.True);
        Assert.That(failedSessionStateObserved, Is.True);
        Assert.That(sessionStoppedCount, Is.EqualTo(1));
        Assert.That(sessionServices.IsDisposed, Is.True);
    }

    [UnityTest]
    public IEnumerator GameMatchContractLoss_EntersErrorAndCoordinatesShutdown()
    {
        yield return StartBootstrapAndWaitUntilReady();

        NetworkSessionShutdownCoordinator shutdownCoordinator =
            GetSinglePersistentComponent<NetworkSessionShutdownCoordinator>();
        NetworkSessionStateMachine sessionStateMachine =
            GetSinglePersistentComponent<NetworkSessionStateMachine>();
        NetworkManager networkManager = runtimeContext.NetworkManager;
        IProjectSceneFlowService sceneFlow = G.Resolve<IProjectSceneFlowService>();

        Task hostStart = runtimeContext.SessionOrchestrator.HostLanAsync();
        yield return WaitForTask(hostStart, "Host startup did not complete.");
        yield return WaitForCondition(
            () => sessionStateMachine.CurrentState == NetworkSessionState.Lobby &&
                  !sceneFlow.HasPendingOperation,
            "Host did not reach Lobby before the Game readiness-loss test.");

        IServiceResolver sessionServices =
            runtimeContext.SessionOrchestrator.SessionServices;
        runtimeContext.SessionOrchestrator.StartGame(
            G.Resolve<IGameMapCatalog>().DefaultMapId);

        yield return WaitForCondition(
            () => sessionStateMachine.CurrentState == NetworkSessionState.InGame &&
                  runtimeContext.GetActiveSceneKind() == ProjectSceneKind.Game &&
                  !sceneFlow.HasPendingOperation &&
                  sessionServices.TryResolve(out IMatchCompletionService _),
            "Host did not reach the ready Game state.");

        NetworkGameFlow gameFlow =
            sessionServices.Resolve<IMatchCompletionService>() as NetworkGameFlow;
        bool errorObserved = false;
        bool failedSessionStateObserved = false;
        int sessionStoppedCount = 0;

        Assert.That(gameFlow, Is.Not.Null);
        Assert.That(gameFlow.NetworkObject.IsSpawned, Is.True);

        runtimeContext.StateMachine.StateChanged += (_, current) =>
            errorObserved |= current == GameState.Error;
        sessionStateMachine.StateChanged += (_, current) =>
            failedSessionStateObserved |= current == NetworkSessionState.Failed;
        shutdownCoordinator.SessionStopped += () => sessionStoppedCount++;

        LogAssert.Expect(
            LogType.Warning,
            new Regex(
                "SessionServiceReadinessPolicy: Game state 'InGame' missing " +
                "required dynamic Session contract\\(s\\): IMatchCompletionService"));

        gameFlow.NetworkObject.Despawn(true);

        yield return WaitForCondition(
            () => runtimeContext.StateMachine.CurrentState == GameState.MainMenu &&
                  runtimeContext.GetActiveSceneKind() == ProjectSceneKind.MainMenu &&
                  !networkManager.IsListening &&
                  !shutdownCoordinator.IsShutdownInProgress,
            "Match readiness loss did not complete coordinated shutdown.");

        Assert.That(errorObserved, Is.True);
        Assert.That(failedSessionStateObserved, Is.True);
        Assert.That(sessionStoppedCount, Is.EqualTo(1));
        Assert.That(sessionServices.IsDisposed, Is.True);
    }

    [UnityTest]
    public IEnumerator PostLoadActionFailure_RollsBackAndShutsDownBeforeLobbyCommit()
    {
        yield return StartBootstrapAndWaitUntilReady();

        ProjectScenePostLoadActionRunner actionRunner =
            GetSinglePersistentComponent<ProjectScenePostLoadActionRunner>();
        NetworkSessionShutdownCoordinator shutdownCoordinator =
            GetSinglePersistentComponent<NetworkSessionShutdownCoordinator>();
        NetworkSessionStateMachine sessionStateMachine =
            GetSinglePersistentComponent<NetworkSessionStateMachine>();
        NetworkManager networkManager = runtimeContext.NetworkManager;
        FieldInfo handlersField = typeof(ProjectScenePostLoadActionRunner).GetField(
            "serverActionHandlers",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(handlersField, Is.Not.Null);

        MonoBehaviour[] originalHandlers =
            (MonoBehaviour[])handlersField.GetValue(actionRunner);
        FailingPostLoadActionHandler failingHandler =
            actionRunner.gameObject.AddComponent<FailingPostLoadActionHandler>();
        MonoBehaviour[] overriddenHandlers = ReplaceServerActionHandler(
            originalHandlers,
            ProjectSceneServerAction.SpawnChatSession,
            failingHandler,
            out IProjectSceneFlowServerActionHandler chatHandler);
        failingHandler.Configure(
            ProjectSceneServerAction.SpawnChatSession,
            ProjectSceneKind.Lobby,
            chatHandler);
        IServiceResolver failedSessionServices = null;
        bool lobbyGameStateCommitted = false;
        bool lobbySessionStateCommitted = false;

        runtimeContext.StateMachine.StateChanged += (_, current) =>
            lobbyGameStateCommitted |= current == GameState.Lobby;
        sessionStateMachine.StateChanged += (_, current) =>
            lobbySessionStateCommitted |= current == NetworkSessionState.Lobby;
        shutdownCoordinator.SessionStarted += () =>
            failedSessionServices = runtimeContext.SessionOrchestrator.SessionServices;

        handlersField.SetValue(
            actionRunner,
            overriddenHandlers);

        LogAssert.Expect(
            LogType.Error,
            new Regex(
                "ProjectSceneFlowService completion source " +
                "'ProjectScenePostLoadActionRunner' failed for scene 'Lobby'.*" +
                "Injected post-load action failure"));

        Task hostStart = runtimeContext.SessionOrchestrator.HostLanAsync();
        yield return WaitForTask(hostStart, "Host startup call did not complete.");
        yield return WaitForCondition(
            () => runtimeContext.StateMachine.CurrentState == GameState.MainMenu &&
                  runtimeContext.GetActiveSceneKind() == ProjectSceneKind.MainMenu &&
                  !networkManager.IsListening &&
                  !shutdownCoordinator.IsShutdownInProgress,
            "Failed post-load action did not complete coordinated shutdown.");

        handlersField.SetValue(actionRunner, originalHandlers);

        Assert.That(failingHandler.ExecuteCount, Is.EqualTo(1));
        Assert.That(failingHandler.RollbackCount, Is.EqualTo(1));
        Assert.That(lobbyGameStateCommitted, Is.False);
        Assert.That(lobbySessionStateCommitted, Is.False);
        Assert.That(failedSessionServices, Is.Not.Null);
        Assert.That(failedSessionServices.IsDisposed, Is.True);
        Assert.That(networkManager.IsClient, Is.False);
        Assert.That(networkManager.IsServer, Is.False);
        Assert.That(G.IsReady, Is.True);

        UnityEngine.Object.Destroy(failingHandler);
        yield return null;
    }

    [UnityTest]
    public IEnumerator PlayerPostLoadActionFailure_RollsBackScopesAndShutsDownBeforeInGameCommit()
    {
        yield return StartBootstrapAndWaitUntilReady();

        NetworkSessionShutdownCoordinator shutdownCoordinator =
            GetSinglePersistentComponent<NetworkSessionShutdownCoordinator>();
        NetworkSessionStateMachine sessionStateMachine =
            GetSinglePersistentComponent<NetworkSessionStateMachine>();
        NetworkManager networkManager = runtimeContext.NetworkManager;
        IProjectSceneFlowService sceneFlow = G.Resolve<IProjectSceneFlowService>();

        Task hostStart = runtimeContext.SessionOrchestrator.HostLanAsync();
        yield return WaitForTask(hostStart, "Host startup did not complete.");
        yield return WaitForCondition(
            () => sessionStateMachine.CurrentState == NetworkSessionState.Lobby &&
                  runtimeContext.GetActiveSceneKind() == ProjectSceneKind.Lobby &&
                  !sceneFlow.HasPendingOperation,
            "Host did not reach Lobby before the player action failure test.");

        IServiceResolver sessionServices =
            runtimeContext.SessionOrchestrator.SessionServices;
        IPlayerScopeRegistry playerRegistry =
            sessionServices.Resolve<IPlayerScopeRegistry>();
        ProjectScenePostLoadActionRunner actionRunner =
            GetSinglePersistentComponent<ProjectScenePostLoadActionRunner>();
        FieldInfo handlersField = typeof(ProjectScenePostLoadActionRunner).GetField(
            "serverActionHandlers",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(handlersField, Is.Not.Null);

        MonoBehaviour[] originalHandlers =
            (MonoBehaviour[])handlersField.GetValue(actionRunner);
        FailingPostLoadActionHandler failingHandler =
            actionRunner.gameObject.AddComponent<FailingPostLoadActionHandler>();
        MonoBehaviour[] overriddenHandlers = ReplaceServerActionHandler(
            originalHandlers,
            ProjectSceneServerAction.SpawnPlayers,
            failingHandler,
            out IProjectSceneFlowServerActionHandler playerHandler);
        failingHandler.Configure(
            ProjectSceneServerAction.SpawnPlayers,
            ProjectSceneKind.Game,
            playerHandler);

        int playerScopesOpened = 0;
        int playerScopesClosed = 0;
        bool inGameStateCommitted = false;
        bool inGameSessionStateCommitted = false;

        playerRegistry.PlayerScopeOpened += _ => playerScopesOpened++;
        playerRegistry.PlayerScopeClosing += _ => playerScopesClosed++;
        runtimeContext.StateMachine.StateChanged += (_, current) =>
            inGameStateCommitted |= current == GameState.InGame;
        sessionStateMachine.StateChanged += (_, current) =>
            inGameSessionStateCommitted |= current == NetworkSessionState.InGame;
        handlersField.SetValue(
            actionRunner,
            overriddenHandlers);

        LogAssert.Expect(
            LogType.Error,
            new Regex(
                "ProjectSceneFlowService completion source " +
                "'ProjectScenePostLoadActionRunner' failed for scene 'Game'.*" +
                "Injected post-load action failure"));

        runtimeContext.SessionOrchestrator.StartGame(
            G.Resolve<IGameMapCatalog>().DefaultMapId);
        yield return WaitForCondition(
            () => runtimeContext.StateMachine.CurrentState == GameState.MainMenu &&
                  runtimeContext.GetActiveSceneKind() == ProjectSceneKind.MainMenu &&
                  !networkManager.IsListening &&
                  !shutdownCoordinator.IsShutdownInProgress,
            "Failed player post-load action did not complete coordinated shutdown.");

        handlersField.SetValue(actionRunner, originalHandlers);

        Assert.That(failingHandler.ExecuteCount, Is.EqualTo(1));
        Assert.That(failingHandler.RollbackCount, Is.EqualTo(1));
        Assert.That(playerScopesOpened, Is.GreaterThan(0));
        Assert.That(playerScopesClosed, Is.EqualTo(playerScopesOpened));
        Assert.That(inGameStateCommitted, Is.False);
        Assert.That(inGameSessionStateCommitted, Is.False);
        Assert.That(sessionServices.IsDisposed, Is.True);
        Assert.That(playerRegistry.IsDisposed, Is.True);
        Assert.That(networkManager.IsClient, Is.False);
        Assert.That(networkManager.IsServer, Is.False);

        UnityEngine.Object.Destroy(failingHandler);
        yield return null;
    }

    private IEnumerator StartBootstrapAndWaitUntilReady()
    {
        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(
            BootstrapScenePath,
            LoadSceneMode.Single);

        Assert.That(loadOperation, Is.Not.Null, "Bootstrap scene could not be loaded.");
        yield return loadOperation;
        yield return WaitForCondition(
            () => G.IsReady,
            "ProjectContext did not publish G after Bootstrap startup.");

        runtimeContext = GetSinglePersistentComponent<ProjectContext>();

        yield return WaitForCondition(
            () => G.TryResolve(out IProjectSceneFlowService flow) &&
                  !flow.HasPendingOperation &&
                  runtimeContext.GetActiveSceneKind() == ProjectSceneKind.MainMenu,
            "Bootstrap startup scene operation did not complete.");
    }

    private T GetSinglePersistentComponent<T>() where T : Component
    {
        List<T> components = new();
        GameObject[] roots = persistentScene.GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            T[] rootComponents = roots[i].GetComponentsInChildren<T>(true);
            components.AddRange(rootComponents);
        }

        Assert.That(
            components.Count,
            Is.EqualTo(1),
            $"Expected exactly one persistent {typeof(T).Name}.");

        return components[0];
    }

    private IEnumerator StopAndDestroyProjectRuntimeRoots()
    {
        if (!persistentScene.IsValid())
            yield break;

        GameObject[] roots = persistentScene.GetRootGameObjects();
        List<GameObject> runtimeRoots = new();
        List<Task> shutdownTasks = new();

        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];

            if (root == null || !IsProjectRuntimeRoot(root))
                continue;

            runtimeRoots.Add(root);
            ProjectContext[] rootContexts =
                root.GetComponentsInChildren<ProjectContext>(true);
            bool runtimeAlreadyDisposed = rootContexts.Length > 0;

            for (int contextIndex = 0;
                 contextIndex < rootContexts.Length && runtimeAlreadyDisposed;
                 contextIndex++)
            {
                runtimeAlreadyDisposed =
                    rootContexts[contextIndex] == null ||
                    rootContexts[contextIndex].LifecycleState ==
                    ProjectRuntimeLifecycleState.Disposed;
            }

            if (runtimeAlreadyDisposed)
                continue;

            NetworkSessionShutdownCoordinator[] coordinators =
                root.GetComponentsInChildren<NetworkSessionShutdownCoordinator>(true);

            for (int coordinatorIndex = 0;
                 coordinatorIndex < coordinators.Length;
                 coordinatorIndex++)
            {
                shutdownTasks.Add(coordinators[coordinatorIndex].ShutdownAndWaitAsync(
                    NetworkShutdownMode.Immediate));
            }
        }

        float timeoutAt = Time.realtimeSinceStartup + OperationTimeoutSeconds;

        while (!AreTasksCompleted(shutdownTasks) &&
               Time.realtimeSinceStartup < timeoutAt)
        {
            yield return null;
        }

        for (int i = 0; i < runtimeRoots.Count; i++)
        {
            ProjectContext[] contexts =
                runtimeRoots[i].GetComponentsInChildren<ProjectContext>(true);

            for (int contextIndex = 0; contextIndex < contexts.Length; contextIndex++)
                contexts[contextIndex]?.DisposeRuntime();
        }

        for (int i = 0; i < runtimeRoots.Count; i++)
        {
            if (runtimeRoots[i] != null)
                UnityEngine.Object.Destroy(runtimeRoots[i]);
        }

        if (runtimeRoots.Count > 0)
            yield return null;
    }

    private static bool AreTasksCompleted(IReadOnlyList<Task> tasks)
    {
        for (int i = 0; i < tasks.Count; i++)
        {
            if (!tasks[i].IsCompleted)
                return false;
        }

        return true;
    }

    private static MonoBehaviour[] ReplaceServerActionHandler(
        MonoBehaviour[] originalHandlers,
        ProjectSceneServerAction action,
        MonoBehaviour replacement,
        out IProjectSceneFlowServerActionHandler replacedHandler)
    {
        if (originalHandlers == null)
            throw new ArgumentNullException(nameof(originalHandlers));

        if (replacement == null)
            throw new ArgumentNullException(nameof(replacement));

        MonoBehaviour[] handlers = (MonoBehaviour[])originalHandlers.Clone();
        replacedHandler = null;
        int replacementIndex = -1;

        for (int i = 0; i < originalHandlers.Length; i++)
        {
            if (originalHandlers[i] is not IProjectSceneFlowServerActionHandler candidate ||
                !candidate.CanHandle(action))
            {
                continue;
            }

            if (replacementIndex >= 0)
            {
                throw new InvalidOperationException(
                    $"Multiple server action handlers can execute '{action}'.");
            }

            replacementIndex = i;
            replacedHandler = candidate;
        }

        if (replacementIndex < 0)
        {
            throw new InvalidOperationException(
                $"No server action handler can execute '{action}'.");
        }

        handlers[replacementIndex] = replacement;
        return handlers;
    }

    private static bool IsProjectRuntimeRoot(GameObject root)
    {
        return root.GetComponentInChildren<ProjectContext>(true) != null ||
               root.GetComponentInChildren<AppRuntime>(true) != null ||
               root.GetComponentInChildren<AudioManager>(true) != null ||
               root.GetComponentInChildren<UiErrorManager>(true) != null;
    }

    private static IEnumerator WaitForCondition(Func<bool> condition, string failureMessage)
    {
        float timeoutAt = Time.realtimeSinceStartup + OperationTimeoutSeconds;

        while (!condition.Invoke() && Time.realtimeSinceStartup < timeoutAt)
            yield return null;

        Assert.That(condition.Invoke(), Is.True, failureMessage);
    }

    private static IEnumerator WaitForTask(Task task, string failureMessage)
    {
        yield return WaitForCondition(() => task.IsCompleted, failureMessage);

        if (task.IsFaulted)
            Assert.Fail($"{failureMessage}\n{task.Exception}");

        Assert.That(task.IsCanceled, Is.False, failureMessage);
    }
}
