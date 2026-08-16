using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Unity.Multiplayer.Playmode;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class MultiplayerBootstrapScenarioProbe : MonoBehaviour
{
    private const string BootstrapScenePath =
        "Assets/Collaborators/Qewzdl/Scenes/Bootstrap.unity";
    private const string HostTag = "GHost";
    private const string ClientTag = "GClient";
    private const string LateClientTag = "GLateClient";
    private const float StepTimeoutSeconds = 40f;

    private bool failed;

    private IEnumerator Start()
    {
        string[] tags = CurrentPlayer.ReadOnlyTags();
        bool isHost = tags.Contains(HostTag);
        bool isClient = tags.Contains(ClientTag);
        bool isLateClient = tags.Contains(LateClientTag);

        if (!isHost && !isClient && !isLateClient)
        {
            Debug.Log(
                $"{nameof(MultiplayerBootstrapScenarioProbe)} is idle. Assign one of " +
                $"'{HostTag}', '{ClientTag}' or '{LateClientTag}' in Multiplayer Play Mode.",
                this);
            yield break;
        }

        DontDestroyOnLoad(gameObject);
        yield return SceneManager.LoadSceneAsync(BootstrapScenePath, LoadSceneMode.Single);
        yield return WaitUntil(
            () => G.IsReady &&
                  G.TryResolve(out IProjectSceneFlowService flow) &&
                  !flow.HasPendingOperation,
            "Bootstrap did not publish a ready G facade.");

        if (failed)
            yield break;

        if (isHost)
            yield return RunHost();
        else
            yield return RunClient(isLateClient);
    }

    private IEnumerator RunHost()
    {
        INetworkSessionService session = G.Resolve<INetworkSessionService>();
        NetworkSessionOrchestrator orchestrator =
            UnityEngine.Object.FindFirstObjectByType<NetworkSessionOrchestrator>();
        NetworkSessionStateMachine sessionState =
            UnityEngine.Object.FindFirstObjectByType<NetworkSessionStateMachine>();
        NetworkManager networkManager =
            UnityEngine.Object.FindFirstObjectByType<NetworkManager>();

        Task hostStart = session.HostLanAsync();
        yield return WaitForTask(hostStart, "Production HostLanAsync failed.");
        yield return WaitUntil(
            () => sessionState != null &&
                  sessionState.CurrentState == NetworkSessionState.Lobby,
            "Host did not commit Lobby readiness.");
        yield return WaitUntil(
            () => networkManager != null && networkManager.ConnectedClientsIds.Count >= 2,
            "First production client did not connect.");

        if (failed)
            yield break;

        session.StartGame(G.Resolve<IGameMapCatalog>().DefaultMapId);
        yield return WaitUntil(
            () => sessionState.CurrentState == NetworkSessionState.InGame,
            "Host did not commit InGame readiness.");

        NetworkObjectiveFlow objectiveFlow =
            UnityEngine.Object.FindFirstObjectByType<NetworkObjectiveFlow>();
        yield return WaitUntil(
            () => objectiveFlow != null && objectiveFlow.HasActiveObjective,
            "Host objective flow did not activate its first objective.");

        if (failed)
            yield break;

        ObjectiveDefinition activeObjective = objectiveFlow.ActiveObjective;

        if (!objectiveFlow.ReportObjectiveProgressServerOnly(
                activeObjective,
                0.5f,
                networkManager.LocalClientId) ||
            !Mathf.Approximately(
                objectiveFlow.CurrentObjective.Progress01,
                0.5f))
        {
            ReportFailure(
                "Host could not commit server-authoritative objective progress.");
            yield break;
        }

        yield return WaitUntil(
            () => networkManager.ConnectedClientsIds.Count >= 3,
            "Late-joining production client did not connect.");
        yield return new WaitForSecondsRealtime(3f);

        if (failed)
            yield break;

        IServiceResolver services = orchestrator != null
            ? orchestrator.SessionServices
            : null;

        if (services == null ||
            !services.TryResolve(out IMatchCompletionService matchService) ||
            matchService is not NetworkGameFlow gameFlow ||
            !gameFlow.IsSpawned)
        {
            ReportFailure("Host could not resolve the spawned production match service.");
            yield break;
        }

        int matchResolvedCount = 0;
        gameFlow.MatchResolved += _ => matchResolvedCount++;

        if (!objectiveFlow.CompleteObjectiveServerOnly(
                activeObjective,
                networkManager.LocalClientId))
        {
            ReportFailure("Host could not complete the active objective.");
            yield break;
        }

        yield return WaitUntil(
            () => objectiveFlow.CurrentObjective.State ==
                      ObjectiveRuntimeState.Completed &&
                  gameFlow.CurrentResult.Source ==
                      MatchResultSource.Objective,
            "Objective completion did not synchronize into the match result.");

        if (failed)
            yield break;

        bool duplicateCompletionAccepted =
            objectiveFlow.CompleteObjectiveServerOnly(
                activeObjective,
                networkManager.LocalClientId);

        if (duplicateCompletionAccepted || matchResolvedCount != 1)
        {
            ReportFailure(
                "Objective completion was accepted or raised MatchResolved more than once.");
            yield break;
        }

        yield return new WaitForSecondsRealtime(1f);
        gameFlow.NetworkObject.Despawn(true);
        yield return WaitUntil(
            () => !networkManager.IsListening &&
                  G.Resolve<IGameStateService>().CurrentState == GameState.MainMenu,
            "Host did not perform coordinated shutdown after contract loss.");

        if (!failed)
            CurrentPlayer.ReportResult(true, "Real host bootstrap lifecycle completed.");
    }

    private IEnumerator RunClient(bool lateJoin)
    {
        if (lateJoin)
            yield return new WaitForSecondsRealtime(8f);
        else
            yield return new WaitForSecondsRealtime(1f);

        INetworkSessionService session = G.Resolve<INetworkSessionService>();
        NetworkSessionOrchestrator orchestrator =
            UnityEngine.Object.FindFirstObjectByType<NetworkSessionOrchestrator>();
        NetworkSessionStateMachine sessionState =
            UnityEngine.Object.FindFirstObjectByType<NetworkSessionStateMachine>();
        bool startingClientObserved = false;
        bool lobbyObserved = false;
        bool inGameObserved = false;

        sessionState.StateChanged += (_, current) =>
        {
            startingClientObserved |= current == NetworkSessionState.StartingClient;
            lobbyObserved |= current == NetworkSessionState.Lobby;
            inGameObserved |= current == NetworkSessionState.InGame;
        };

        Task join = session.JoinLanAsync("127.0.0.1");
        yield return WaitForTask(join, "Production JoinLanAsync failed.");

        if (!lateJoin)
        {
            yield return WaitUntil(
                () => sessionState.CurrentState == NetworkSessionState.Lobby,
                "Client did not commit Lobby readiness.");
        }

        yield return WaitUntil(
            () => sessionState.CurrentState == NetworkSessionState.InGame,
            lateJoin
                ? "Late client did not synchronize directly into InGame."
                : "Client did not commit InGame readiness.");

        NetworkObjectiveFlow objectiveFlow =
            UnityEngine.Object.FindFirstObjectByType<NetworkObjectiveFlow>();
        yield return WaitUntil(
            () => objectiveFlow != null &&
                  objectiveFlow.CurrentObjective.State ==
                      ObjectiveRuntimeState.Active &&
                  Mathf.Approximately(
                      objectiveFlow.CurrentObjective.Progress01,
                      0.5f),
            lateJoin
                ? "Late client did not receive the current objective snapshot."
                : "Client did not receive server-authoritative objective progress.");

        IServiceResolver services = orchestrator != null
            ? orchestrator.SessionServices
            : null;
        bool phaseReady = services != null &&
                          services.TryResolve(out ISessionPhaseService phase) &&
                          phase is NetworkSessionPhaseService &&
                          phase.ServerScenePhase == ProjectSceneKind.Game;

        if (!startingClientObserved || !inGameObserved ||
            (!lateJoin && !lobbyObserved) || !phaseReady)
        {
            ReportFailure(
                "Client production state history or authoritative phase was incomplete.");
            yield break;
        }

        yield return WaitUntil(
            () => objectiveFlow.CurrentObjective.State ==
                      ObjectiveRuntimeState.Completed &&
                  services.TryResolve(
                      out IMatchCompletionService matchService) &&
                  matchService is NetworkGameFlow gameFlow &&
                  gameFlow.CurrentResult.Source ==
                      MatchResultSource.Objective,
            "Client did not synchronize objective match completion.");

        if (failed)
            yield break;

        yield return WaitUntil(
            () => G.Resolve<IGameStateService>().CurrentState == GameState.MainMenu &&
                  sessionState.CurrentState == NetworkSessionState.Offline,
            "Client did not shut down after losing a required Session contract.");

        if (!failed)
        {
            CurrentPlayer.ReportResult(
                true,
                lateJoin
                    ? "Real late-client bootstrap lifecycle completed."
                    : "Real client bootstrap lifecycle completed.");
        }
    }

    private IEnumerator WaitForTask(Task task, string failureMessage)
    {
        yield return WaitUntil(() => task.IsCompleted, failureMessage);

        if (!failed && task.IsFaulted)
            ReportFailure(task.Exception?.GetBaseException().Message ?? failureMessage);
    }

    private IEnumerator WaitUntil(Func<bool> condition, string failureMessage)
    {
        float deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;

        while (!condition.Invoke() && Time.realtimeSinceStartup < deadline)
            yield return null;

        if (!condition.Invoke())
            ReportFailure(failureMessage);
    }

    private void ReportFailure(string message)
    {
        if (failed)
            return;

        failed = true;
        Debug.LogError(message, this);
        CurrentPlayer.ReportResult(false, message);
    }
}
