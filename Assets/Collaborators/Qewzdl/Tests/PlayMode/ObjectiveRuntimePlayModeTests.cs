using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class ObjectiveRuntimePlayModeTests
{
    private const string BootstrapScenePath =
        "Assets/Collaborators/Qewzdl/Scenes/Bootstrap.unity";
    private const float OperationTimeoutSeconds = 20f;

    private Scene persistentScene;
    private GameObject persistentSceneProbe;
    private ProjectContext runtimeContext;
    private NetworkObjectiveFlow objectiveFlow;
    private NetworkGameFlow gameFlow;
    private readonly List<UnityEngine.Object> cleanup = new();

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        persistentSceneProbe = new GameObject("Objective runtime PlayMode test probe");
        UnityEngine.Object.DontDestroyOnLoad(persistentSceneProbe);
        persistentScene = persistentSceneProbe.scene;

        yield return StopAndDestroyProjectRuntimeRoots();

        G.ResetRuntimeState();
        runtimeContext = null;
        objectiveFlow = null;
        gameFlow = null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        for (int i = cleanup.Count - 1; i >= 0; i--)
        {
            if (cleanup[i] != null)
                UnityEngine.Object.Destroy(cleanup[i]);
        }

        cleanup.Clear();

        yield return StopAndDestroyProjectRuntimeRoots();

        G.ResetRuntimeState();
        runtimeContext = null;
        objectiveFlow = null;
        gameFlow = null;

        if (persistentSceneProbe != null)
            UnityEngine.Object.Destroy(persistentSceneProbe);

        yield return null;
    }

    // What a match does about an objective belongs to the list it sits in, so
    // the same objective hands over in the middle of one and wins at the end of
    // another. Nothing about the asset says which.
    [UnityTest]
    public IEnumerator ObjectiveEndsTheMatchOnlyWhenTheSequenceRunsOut()
    {
        yield return StartHostedMatchAndWaitForActiveObjective();

        ObjectiveDefinition first = GetProductionObjective(0);
        ObjectiveDefinition second = CloneObjective(first);
        second.name = "Second step";
        PlayModeTestReflection.SetField(second, "requiresSceneBinding", false);
        UseObjectiveSequence(first, second);

        Assert.That(
            objectiveFlow.CompleteObjectiveServerOnly(
                objectiveFlow.ActiveObjective,
                LocalClientId),
            Is.True);

        // Something is still left, so completing this one only moves along.
        Assert.That(objectiveFlow.CurrentObjective.SequenceIndex, Is.EqualTo(1));
        Assert.That(
            objectiveFlow.CurrentObjective.State,
            Is.EqualTo(ObjectiveRuntimeState.Active));
        Assert.That(gameFlow.IsMatchRunning, Is.True);

        Assert.That(
            objectiveFlow.CompleteObjectiveServerOnly(
                objectiveFlow.ActiveObjective,
                LocalClientId),
            Is.True);

        Assert.That(
            objectiveFlow.CurrentObjective.State,
            Is.EqualTo(ObjectiveRuntimeState.Completed));
        Assert.That(
            gameFlow.CurrentResult.Source,
            Is.EqualTo(MatchResultSource.Objective));
        Assert.That(
            gameFlow.CurrentResult.ResultType,
            Is.EqualTo(ProductionSequence.CompletionResult));
    }

    // A lost objective is a gameplay result, not a broken flow: it resolves the
    // match and leaves the session running instead of faulting it down.
    [UnityTest]
    public IEnumerator FailedObjective_ResolvesMatchAsDefeatWithoutFaultingTheFlow()
    {
        yield return StartHostedMatchAndWaitForActiveObjective();

        ObjectiveDefinition losableObjective = CloneObjective(GetProductionObjective(0));
        UseObjectiveSequence(
            GameResultType.Defeat,
            "Objective timer ran out",
            losableObjective);

        Assert.That(
            objectiveFlow.FailObjectiveServerOnly(
                objectiveFlow.ActiveObjective,
                LocalClientId),
            Is.True);

        Assert.That(
            objectiveFlow.CurrentObjective.State,
            Is.EqualTo(ObjectiveRuntimeState.Failed));
        Assert.That(
            gameFlow.CurrentResult.Source,
            Is.EqualTo(MatchResultSource.Objective));
        Assert.That(
            gameFlow.CurrentResult.ResultType,
            Is.EqualTo(GameResultType.Defeat));
        Assert.That(
            gameFlow.CurrentResult.Reason.ToString(),
            Is.EqualTo("Objective timer ran out"));
        Assert.That(objectiveFlow.IsServerReady, Is.True);
    }

    // A finished match used to leave everyone standing in the game scene with
    // nothing left to do and no way out but the pause menu.
    [UnityTest]
    public IEnumerator FinishedMatch_ReturnsToTheLobbyWithTheSessionStillUp()
    {
        yield return StartHostedMatchAndWaitForActiveObjective();

        NetworkManager networkManager = runtimeContext.NetworkManager;

        Assert.That(
            objectiveFlow.CompleteObjectiveServerOnly(
                objectiveFlow.ActiveObjective,
                LocalClientId),
            Is.True);

        yield return WaitForCondition(
            () => runtimeContext.StateMachine.CurrentState == GameState.Lobby &&
                  runtimeContext.GetActiveSceneKind() == ProjectSceneKind.Lobby,
            "A finished match never took the session back to the lobby.");

        // The point of going back rather than out: another round costs a press
        // of start, not hosting and rejoining.
        Assert.That(
            networkManager.IsListening,
            Is.True,
            "The session went down with the match.");

        NetworkSessionStateMachine sessionStateMachine =
            GetSinglePersistentComponent<NetworkSessionStateMachine>();
        Assert.That(
            sessionStateMachine.CurrentState,
            Is.EqualTo(NetworkSessionState.Lobby));
        Assert.That(sessionStateMachine.CanStartGame, Is.True);
    }

    [UnityTest]
    public IEnumerator MatchCompletion_IsRefusedOutsidePlayingAndResolvesOnlyOnce()
    {
        yield return StartHostedMatchAndWaitForActiveObjective();

        int matchResolvedCount = 0;
        gameFlow.MatchResolved += _ => matchResolvedCount++;

        LogAssert.Expect(
            LogType.Error,
            new Regex("NetworkGameFlow received invalid match result\\."));
        Assert.That(
            gameFlow.CompleteMatchServerOnly(GameResultData.None, "Nothing happened"),
            Is.False);
        Assert.That(gameFlow.CurrentPhase, Is.EqualTo(GamePhase.Playing));
        Assert.That(gameFlow.StartMatchServerOnly(), Is.False);

        Assert.That(
            objectiveFlow.CompleteObjectiveServerOnly(
                objectiveFlow.ActiveObjective,
                LocalClientId),
            Is.True);
        Assert.That(gameFlow.CurrentPhase, Is.EqualTo(GamePhase.MatchResolved));

        // A resolved match is refused quietly - the finished check runs before
        // the phase check, so this second result never reaches it.
        Assert.That(
            gameFlow.CompleteMatchServerOnly(
                GameResultData.Create(
                    GameResultType.Defeat,
                    MatchResultSource.PlayerCaught,
                    "player_caught",
                    "Second result",
                    LocalClientId),
                "Second result"),
            Is.False);

        Assert.That(gameFlow.StartMatchServerOnly(), Is.False);
        Assert.That(matchResolvedCount, Is.EqualTo(1));
        Assert.That(
            gameFlow.CurrentResult.Source,
            Is.EqualTo(MatchResultSource.Objective));
    }

    [UnityTest]
    public IEnumerator TimedReporter_CompletesTheObjectiveWhenItsTimerRunsOut()
    {
        yield return StartHostedMatchAndWaitForActiveObjective();

        CreateTimedReporter(completesWhenTimerEnds: true);

        yield return WaitForCondition(
            () => gameFlow.CurrentResult.Source == MatchResultSource.Objective,
            "The timed reporter did not complete its objective.");

        Assert.That(
            objectiveFlow.CurrentObjective.State,
            Is.EqualTo(ObjectiveRuntimeState.Completed));
        Assert.That(
            gameFlow.CurrentResult.ResultType,
            Is.EqualTo(ProductionSequence.CompletionResult));
    }

    // Reporting full progress completes an objective by itself, so a timer that
    // is meant to be lost has to stop short of it.
    [UnityTest]
    public IEnumerator TimedReporter_LosesTheObjectiveWhenItsTimerRunsOut()
    {
        yield return StartHostedMatchAndWaitForActiveObjective();

        CreateTimedReporter(completesWhenTimerEnds: false);

        yield return WaitForCondition(
            () => gameFlow.CurrentResult.Source == MatchResultSource.Objective,
            "The timed reporter did not resolve the match on its deadline.");

        Assert.That(
            objectiveFlow.CurrentObjective.State,
            Is.EqualTo(ObjectiveRuntimeState.Failed));
        Assert.That(
            gameFlow.CurrentResult.ResultType,
            Is.EqualTo(GameResultType.Defeat));
    }

    [UnityTest]
    public IEnumerator TriggerZoneReporter_CountsTheSameClientOnlyOnce()
    {
        yield return StartHostedMatchAndWaitForActiveObjective();

        NetworkManager networkManager = runtimeContext.NetworkManager;
        yield return WaitForCondition(
            () => networkManager.LocalClient?.PlayerObject != null &&
                  networkManager.LocalClient.PlayerObject
                      .GetComponentInChildren<Collider>() != null,
            "The hosted player object never got a collider to enter with.");

        ObjectiveSceneBinding binding = GetActiveBinding();
        ObjectiveDefinition twoEntryObjective = CloneObjective(GetProductionObjective(0));
        PlayModeTestReflection.SetField(twoEntryObjective, "requiredProgress", 2f);
        PlayModeTestReflection.SetField(binding, "objective", twoEntryObjective);
        UseObjectiveSequence(twoEntryObjective);

        GameObject zone = Track(new GameObject("Objective trigger zone"));
        zone.SetActive(false);
        zone.AddComponent<BoxCollider>();
        TriggerZoneObjectiveReporter reporter =
            zone.AddComponent<TriggerZoneObjectiveReporter>();
        PlayModeTestReflection.SetField(reporter, "objectiveBinding", binding);
        PlayModeTestReflection.SetField(reporter, "requiredTag", string.Empty);
        PlayModeTestReflection.SetField(reporter, "countUniqueClients", true);
        zone.SetActive(true);

        Collider playerCollider = networkManager.LocalClient.PlayerObject
            .GetComponentInChildren<Collider>();

        PlayModeTestReflection.Invoke(reporter, "OnTriggerEnter", playerCollider);
        Assert.That(
            objectiveFlow.CurrentObjective.Progress01,
            Is.EqualTo(0.5f).Within(0.001f));

        PlayModeTestReflection.Invoke(reporter, "OnTriggerEnter", playerCollider);
        Assert.That(
            objectiveFlow.CurrentObjective.Progress01,
            Is.EqualTo(0.5f).Within(0.001f),
            "The same client entering twice must not count twice.");
        Assert.That(
            objectiveFlow.CurrentObjective.State,
            Is.EqualTo(ObjectiveRuntimeState.Active));
        Assert.That(gameFlow.CurrentResult.HasResult, Is.False);
    }

    // Handle progress and the unlock are two different events. Reporting the
    // last handle as full progress would complete the objective while the door
    // is still locked.
    [UnityTest]
    public IEnumerator EntranceDoorReporter_HoldsProgressBackUntilTheDoorOpens()
    {
        yield return StartHostedMatchAndWaitForActiveObjective();

        EntranceDoorObjectiveReporter reporter =
            UnityEngine.Object.FindFirstObjectByType<EntranceDoorObjectiveReporter>();
        Assert.That(
            reporter,
            Is.Not.Null,
            "The production map no longer reports the entrance door objective.");

        EntranceDoor door = UnityEngine.Object.FindFirstObjectByType<EntranceDoor>();
        Assert.That(door, Is.Not.Null);
        Assert.That(door.IsUnlocked, Is.False);

        PlayModeTestReflection.Invoke(
            reporter,
            "HandleDoorHandleInserted",
            1,
            door.RequiredHandleCount,
            door.RequiredHandleCount,
            LocalClientId);

        Assert.That(
            objectiveFlow.CurrentObjective.State,
            Is.EqualTo(ObjectiveRuntimeState.Active),
            "A locked door must not complete its objective.");
        Assert.That(objectiveFlow.CurrentObjective.Progress01, Is.LessThan(1f));
        Assert.That(gameFlow.CurrentResult.HasResult, Is.False);

        PlayModeTestReflection.Invoke(reporter, "HandleDoorUnlocked", LocalClientId);

        Assert.That(
            objectiveFlow.CurrentObjective.State,
            Is.EqualTo(ObjectiveRuntimeState.Completed));
        Assert.That(
            gameFlow.CurrentResult.Source,
            Is.EqualTo(MatchResultSource.Objective));
    }

    private ulong LocalClientId => runtimeContext.NetworkManager.LocalClientId;

    private TimedObjectiveReporter CreateTimedReporter(bool completesWhenTimerEnds)
    {
        GameObject host = Track(new GameObject("Objective timer"));
        host.SetActive(false);
        TimedObjectiveReporter reporter = host.AddComponent<TimedObjectiveReporter>();
        PlayModeTestReflection.SetField(reporter, "objectiveBinding", GetActiveBinding());
        PlayModeTestReflection.SetField(
            reporter,
            "useObjectiveRequiredProgressAsDuration",
            false);
        PlayModeTestReflection.SetField(reporter, "durationSeconds", 0.1f);
        PlayModeTestReflection.SetField(reporter, "progressReportInterval", 0.05f);
        PlayModeTestReflection.SetField(
            reporter,
            "completeWhenTimerEnds",
            completesWhenTimerEnds);
        PlayModeTestReflection.SetField(
            reporter,
            "failWhenTimerEnds",
            !completesWhenTimerEnds);
        host.SetActive(true);
        return reporter;
    }

    private ObjectiveSceneBinding GetActiveBinding()
    {
        ObjectiveSceneBinding binding =
            UnityEngine.Object.FindFirstObjectByType<ObjectiveSceneBinding>();
        Assert.That(binding, Is.Not.Null, "The production map has no objective binding.");
        Assert.That(binding.IsActive, Is.True, "The objective binding is not active.");
        return binding;
    }

    private ObjectiveDefinition GetProductionObjective(int index)
    {
        return ProductionSequence.GetObjective(index);
    }

    private ObjectiveDefinition CloneObjective(ObjectiveDefinition source)
    {
        return Track(UnityEngine.Object.Instantiate(source));
    }

    private void UseObjectiveSequence(params ObjectiveDefinition[] objectives)
    {
        UseObjectiveSequence(GameResultType.Defeat, "Objective failed", objectives);
    }

    private void UseObjectiveSequence(
        GameResultType failureResult,
        string failureReason,
        params ObjectiveDefinition[] objectives)
    {
        ObjectiveSequenceDefinition sequence =
            Track(ScriptableObject.CreateInstance<ObjectiveSequenceDefinition>());
        PlayModeTestReflection.SetField(sequence, "objectives", objectives);
        PlayModeTestReflection.SetField(
            sequence,
            "completionResult",
            ProductionSequence.CompletionResult);
        PlayModeTestReflection.SetField(
            sequence,
            "completionReason",
            ProductionSequence.CompletionReason);
        PlayModeTestReflection.SetField(sequence, "failureResult", failureResult);
        PlayModeTestReflection.SetField(sequence, "failureReason", failureReason);
        PlayModeTestReflection.SetField(
            objectiveFlow,
            "activeObjectiveSequence",
            sequence);
    }

    private ObjectiveSequenceDefinition ProductionSequence =>
        PlayModeTestReflection
            .GetField<ObjectiveSequenceDefinition>(objectiveFlow, "objectiveSequence");

    private IEnumerator StartHostedMatchAndWaitForActiveObjective()
    {
        yield return StartBootstrapAndWaitUntilReady();

        NetworkSessionStateMachine sessionStateMachine =
            GetSinglePersistentComponent<NetworkSessionStateMachine>();
        IProjectSceneFlowService sceneFlow = G.Resolve<IProjectSceneFlowService>();

        Task hostStart = runtimeContext.SessionOrchestrator.HostLanAsync();
        yield return WaitForTask(hostStart, "Host startup did not complete.");
        yield return WaitForCondition(
            () => sessionStateMachine.CurrentState == NetworkSessionState.Lobby &&
                  !sceneFlow.HasPendingOperation,
            "Host did not reach Lobby before the objective test.");

        runtimeContext.SessionOrchestrator.StartGame(
            G.Resolve<IGameMapCatalog>().DefaultMapId);

        objectiveFlow = null;
        yield return WaitForCondition(
            () =>
            {
                objectiveFlow =
                    UnityEngine.Object.FindFirstObjectByType<NetworkObjectiveFlow>();
                return sessionStateMachine.CurrentState ==
                           NetworkSessionState.InGame &&
                       objectiveFlow != null &&
                       objectiveFlow.HasActiveObjective &&
                       !sceneFlow.HasPendingOperation;
            },
            "Host did not reach the active production objective.");

        gameFlow = runtimeContext.SessionOrchestrator.SessionServices
            .Resolve<IMatchCompletionService>() as NetworkGameFlow;
        Assert.That(gameFlow, Is.Not.Null);
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
            components.AddRange(roots[i].GetComponentsInChildren<T>(true));

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

    private static bool IsProjectRuntimeRoot(GameObject root)
    {
        return root.GetComponentInChildren<ProjectContext>(true) != null ||
               root.GetComponentInChildren<AppRuntime>(true) != null ||
               root.GetComponentInChildren<AudioManager>(true) != null ||
               root.GetComponentInChildren<UiErrorManager>(true) != null ||
               root.GetComponentInChildren<SettingsDocument>(true) != null;
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

    private T Track<T>(T value)
        where T : UnityEngine.Object
    {
        cleanup.Add(value);
        return value;
    }
}
