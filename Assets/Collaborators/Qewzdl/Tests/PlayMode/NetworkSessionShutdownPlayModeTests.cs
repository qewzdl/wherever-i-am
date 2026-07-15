using System;
using System.Collections;
using System.Collections.Generic;
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

        Task hostStart = runtimeContext.SessionOrchestrator.HostLanAsync();
        yield return WaitForTask(hostStart, "Host startup did not complete.");
        yield return WaitForCondition(
            () => sessionStateMachine.CurrentState == NetworkSessionState.Lobby &&
                  runtimeContext.GetActiveSceneKind() == ProjectSceneKind.Lobby &&
                  !sceneFlow.HasPendingOperation,
            "Host did not reach the ready Lobby state.");

        IServiceResolver sessionServices =
            runtimeContext.SessionOrchestrator.SessionServices;
        Assert.That(sessionServices, Is.Not.Null);
        Assert.That(sessionServices.IsDisposed, Is.False);

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
        Assert.That(appRuntime.SceneScopeCount, Is.GreaterThan(0));

        List<string> lifecycle = new();
        int clientStoppedCount = 0;
        int serverStoppedCount = 0;
        int playerClosingCount = 0;
        int sessionStoppedCount = 0;
        bool callbacksObservedCanceledOperations = true;
        bool callbacksObservedLiveScopes = true;
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

        Task shutdown = shutdownCoordinator.ShutdownAndWaitAsync();
        Task repeatedShutdown = shutdownCoordinator.ShutdownAndWaitAsync();

        Assert.That(repeatedShutdown, Is.SameAs(shutdown));
        Assert.That(sceneFlow.HasPendingOperation, Is.False);
        Assert.That(mapService.HasPendingOperation, Is.False);

        yield return WaitForTask(shutdown, "Coordinated host shutdown did not complete.");
        yield return WaitForCondition(
            () => runtimeContext.StateMachine.CurrentState == GameState.MainMenu &&
                  runtimeContext.GetActiveSceneKind() == ProjectSceneKind.MainMenu,
            "MainMenu was not activated after host cleanup.");

        Assert.That(clientStoppedCount, Is.EqualTo(1));
        Assert.That(serverStoppedCount, Is.EqualTo(1));
        Assert.That(callbacksObservedCanceledOperations, Is.True);
        Assert.That(callbacksObservedLiveScopes, Is.True);
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
