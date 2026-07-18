using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using UnityEngine;

internal enum NetworkSoakRole
{
    None = 0,
    Host = 1,
    ClientA = 2,
    ClientB = 3
}

internal enum NetworkSoakFault
{
    MapLoading = 0,
    Objective = 1,
    Drag = 2,
    EnemyAttack = 3
}

[Serializable]
internal sealed class NetworkSoakRoleResult
{
    public string role;
    public bool succeeded;
    public string message;
    public string exception;
    public string unityVersion;
    public float durationSeconds;
    public int completedCycles;
    public int disconnects;
    public int reconnects;
    public int maxSpawnedObjects;
    public int maxSceneScopes;
    public string[] faults;
}

/// <summary>
/// Test-only production-process soak harness. It is compiled only into the
/// Development Player built with WIA_PRODUCTION_BOOTSTRAP_TEST.
/// </summary>
[DefaultExecutionOrder(-1990)]
internal sealed class NetworkSoakProcessHarness : MonoBehaviour
{
    private const string RoleArgument = "-gNetworkSoakRole";
    private const string RunDirectoryArgument = "-gNetworkSoakRunDirectory";
    private const string DurationArgument = "-gNetworkSoakDurationSeconds";
    private const string StepTimeoutArgument = "-gNetworkSoakStepTimeoutSeconds";
    private const string LatencyArgument = "-gNetworkSoakLatencyMs";
    private const string JitterArgument = "-gNetworkSoakJitterMs";
    private const string PacketLossArgument = "-gNetworkSoakPacketLossPercent";
    private const float DefaultDurationSeconds = 900f;
    private const float DefaultStepTimeoutSeconds = 180f;
    private const uint DefaultLatencyMs = 80;
    private const uint DefaultJitterMs = 20;
    private const float DefaultPacketLossPercent = 2f;
    private const int RequiredFaultCount = 4;

    private static readonly FieldInfo DraggingStateField =
        typeof(DraggableObject).GetField(
            "netIsDragging",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly List<string> completedFaults = new();

    private NetworkSoakRole role;
    private string roleName;
    private string runDirectory;
    private float requestedDurationSeconds;
    private float stepTimeoutSeconds;
    private uint latencyMs;
    private uint jitterMs;
    private float packetLossPercent;
    private float startedAt;
    private ProjectContext context;
    private AppRuntime appRuntime;
    private NetworkSessionStateMachine sessionState;
    private NetworkManager networkManager;
    private INetworkSessionService session;
    private ServiceScope globalScope;
    private int baselineGlobalChildren;
    private int baselineGlobalServices;
    private int baselineGlobalRegistrationOrder;
    private int baselineSceneScopes;
    private int baselineNetworkObjects;
    private int completedCycles;
    private int disconnects;
    private int reconnects;
    private int maxSpawnedObjects;
    private int maxSceneScopes;
    private bool resultWritten;
    private ulong preparedDragObjectId;
    private ulong attackedPlayerObjectId;
    private int visibleCycle;
    private string visiblePhase = "Bootstrapping";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void TryCreate()
    {
        string[] arguments = Environment.GetCommandLineArgs();

        if (!TryReadRole(arguments, out NetworkSoakRole parsedRole) ||
            !TryReadArgument(arguments, RunDirectoryArgument, out string resultDirectory))
        {
            return;
        }

        GameObject root = new(nameof(NetworkSoakProcessHarness));
        DontDestroyOnLoad(root);

        NetworkSoakProcessHarness harness =
            root.AddComponent<NetworkSoakProcessHarness>();
        harness.role = parsedRole;
        harness.roleName = GetRoleName(parsedRole);
        harness.runDirectory = Path.GetFullPath(resultDirectory);
        harness.requestedDurationSeconds = ReadFloat(
            arguments,
            DurationArgument,
            DefaultDurationSeconds,
            20f,
            1800f);
        harness.stepTimeoutSeconds = ReadFloat(
            arguments,
            StepTimeoutArgument,
            DefaultStepTimeoutSeconds,
            30f,
            600f);
        harness.latencyMs = ReadUInt(
            arguments,
            LatencyArgument,
            DefaultLatencyMs,
            2000);
        harness.jitterMs = ReadUInt(
            arguments,
            JitterArgument,
            DefaultJitterMs,
            1000);
        harness.packetLossPercent = ReadFloat(
            arguments,
            PacketLossArgument,
            DefaultPacketLossPercent,
            0f,
            20f);
    }

    private async void Start()
    {
        startedAt = Time.realtimeSinceStartup;
        Application.runInBackground = true;

        try
        {
            Directory.CreateDirectory(runDirectory);
            await BindProductionRuntimeAsync();
            await WaitForMainMenuAsync();
            CaptureCleanBaseline();
            session = G.Resolve<INetworkSessionService>();

            if (role == NetworkSoakRole.Host)
                await RunHostAsync();
            else
                await RunClientAsync(role == NetworkSoakRole.ClientB);

            Complete(true, "Network soak lifecycle completed without leaks.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            Complete(false, exception.Message, exception);
        }
    }

    private void OnGUI()
    {
        if (Application.isBatchMode || resultWritten)
            return;

        Rect panel = new(12f, 12f, 420f, 148f);
        GUI.Box(panel, GUIContent.none);
        GUI.Label(
            new Rect(28f, 24f, 388f, 24f),
            $"Network Soak - {roleName}");
        GUI.Label(
            new Rect(28f, 50f, 388f, 24f),
            $"Cycle: {visibleCycle}    Phase: {visiblePhase}");
        GUI.Label(
            new Rect(28f, 76f, 388f, 24f),
            $"Network: {latencyMs} ms, jitter {jitterMs} ms, loss {packetLossPercent:F1}%");
        GUI.Label(
            new Rect(28f, 102f, 200f, 24f),
            $"Elapsed: {Mathf.Max(0f, Time.realtimeSinceStartup - startedAt):F0} s");

        if (GUI.Button(
                new Rect(250f, 102f, 166f, 30f),
                "Stop network soak"))
        {
            Complete(
                false,
                $"Network soak was canceled from the {roleName} window.");
        }
    }

    private async Task RunHostAsync()
    {
        int cycle = 0;

        while (true)
        {
            NetworkSoakFault fault =
                (NetworkSoakFault)(cycle % RequiredFaultCount);
            preparedDragObjectId = 0;
            attackedPlayerObjectId = 0;

            await session.HostLanAsync();
            ThrowIfRuntimeEnteredError($"Host startup for cycle {cycle}");
            await WaitUntilAsync(
                () => networkManager.IsHost &&
                      networkManager.IsListening &&
                      networkManager.IsConnectedClient,
                $"Cycle {cycle}: production host did not start.");
            ApplyNetworkSimulation();
            ReportCyclePhase(cycle, "network");

            await WaitForReadySceneAsync(ProjectSceneKind.Lobby);
            ReportCyclePhase(cycle, "lobby");
            await WaitForMarkerAsync(cycle, "client-a", "lobby");
            await WaitForMarkerAsync(cycle, "client-b", "lobby");
            await WaitUntilAsync(
                () => networkManager.ConnectedClientsIds.Count == 3,
                $"Cycle {cycle}: Host did not receive both clients in Lobby.");

            session.StartGame(G.Resolve<IGameMapCatalog>().DefaultMapId);

            if (fault == NetworkSoakFault.MapLoading)
            {
                await WaitForMarkerAsync(cycle, "client-b", "fault-observed");
                await DisconnectFaultClientAsync(cycle);
                await WaitForMarkerAsync(cycle, "client-b", "disconnected");

                // NGO reports an incomplete scene event when a participant
                // disappears from an in-flight map load. Production handles
                // this fail-closed: rollback, coordinated shutdown, MainMenu.
                await WaitForCleanMainMenuAsync();
                await WaitForMarkerAsync(cycle, "client-a", "clean");
                await WaitForMarkerAsync(cycle, "client-b", "clean");
                ValidateCleanBaseline(cycle);
                FinishHostCycle(cycle, fault);

                if (ShouldFinishSoak())
                {
                    WriteAtomic(
                        GetProtocolPath("soak.complete.signal"),
                        DateTime.UtcNow.ToString("O"));
                    return;
                }

                cycle++;
                continue;
            }

            await WaitForReadySceneAsync(ProjectSceneKind.Game);
            FreezeAutonomousEnemySimulation();
            TrackRuntimeHighWatermarks();
            ReportCyclePhase(cycle, "game");
            await WaitForMarkerAsync(cycle, "client-a", "game");

            if (fault != NetworkSoakFault.MapLoading)
            {
                await WaitForMarkerAsync(cycle, "client-b", "game");
                PrepareFault(fault);
                ReportCyclePhase(cycle, "fault-ready");
            }

            await DisconnectFaultClientAsync(cycle);
            await WaitForMarkerAsync(cycle, "client-b", "disconnected");
            await WaitUntilAsync(
                () => networkManager.ConnectedClientsIds.Count == 2,
                $"Cycle {cycle}: Host did not observe Client B disconnect.");
            ValidateRemoteDisconnectCleanup(fault);
            ReportCyclePhase(cycle, "reconnect");

            await WaitForMarkerAsync(cycle, "client-b", "rejoined-game");
            await WaitUntilAsync(
                () => networkManager.ConnectedClientsIds.Count == 3,
                $"Cycle {cycle}: Client B did not reconnect.");
            reconnects++;
            TrackRuntimeHighWatermarks();

            await CommitAndSynchronizeObjectiveProgressAsync(cycle);
            ReportCyclePhase(cycle, "shutdown-signal");

            NetworkShutdownResult shutdown =
                await session.ShutdownToMainMenuAsync();
            ValidateShutdownResult(shutdown, $"host cycle {cycle}");
            await WaitForCleanMainMenuAsync();
            ReportCyclePhase(cycle, "clean");
            await WaitForMarkerAsync(cycle, "client-a", "clean");
            await WaitForMarkerAsync(cycle, "client-b", "clean");
            ValidateCleanBaseline(cycle);

            FinishHostCycle(cycle, fault);

            if (ShouldFinishSoak())
            {
                WriteAtomic(
                    GetProtocolPath("soak.complete.signal"),
                    DateTime.UtcNow.ToString("O"));
                return;
            }

            cycle++;
        }
    }

    private async Task RunClientAsync(bool faultClient)
    {
        int cycle = 0;

        while (true)
        {
            bool shouldRunCycle = await WaitForNextCycleAsync(cycle);

            if (!shouldRunCycle)
                return;

            await session.JoinLanAsync("127.0.0.1");
            ThrowIfRuntimeEnteredError($"Client startup for cycle {cycle}");
            await WaitUntilAsync(
                () => networkManager.IsClient &&
                      !networkManager.IsServer &&
                      networkManager.IsListening &&
                      networkManager.IsConnectedClient,
                $"Cycle {cycle}: {roleName} did not connect.");
            ApplyNetworkSimulation();

            await WaitForReadySceneAsync(ProjectSceneKind.Lobby);
            ReportCyclePhase(cycle, "lobby");

            NetworkSoakFault fault =
                (NetworkSoakFault)(cycle % RequiredFaultCount);

            if (faultClient && fault == NetworkSoakFault.MapLoading)
            {
                await WaitUntilAsync(
                    () => sessionState.CurrentState ==
                          NetworkSessionState.LoadingGame,
                    $"Cycle {cycle}: Client B did not enter map loading.");
                ReportCyclePhase(cycle, "fault-observed");
                await RequestDisconnectAndRecoverAsync(cycle);
                ReportCyclePhase(cycle, "clean");
                await WaitForMarkerAsync(cycle, "host", "complete");
                CompleteClientCycle(fault);
                cycle++;
                continue;
            }

            if (!faultClient && fault == NetworkSoakFault.MapLoading)
            {
                // The server rejects the incomplete NGO map event and runs
                // the normal failure shutdown chain for the remaining client.
                await WaitForCleanMainMenuAsync();
                ValidateCleanBaseline(cycle);
                ReportCyclePhase(cycle, "clean");
                await WaitForMarkerAsync(cycle, "host", "complete");
                CompleteClientCycle(fault);
                cycle++;
                continue;
            }

            await WaitForReadySceneAsync(ProjectSceneKind.Game);
            TrackRuntimeHighWatermarks();
            ReportCyclePhase(cycle, "game");

            if (faultClient)
            {
                await WaitForMarkerAsync(cycle, "host", "fault-ready");
                ReportCyclePhase(cycle, "fault-observed");
                await RequestDisconnectAndRecoverAsync(cycle);
            }

            if (faultClient)
            {
                await WaitForMarkerAsync(cycle, "host", "reconnect");
                await session.JoinLanAsync("127.0.0.1");
                await WaitUntilAsync(
                    () => networkManager.IsClient &&
                          networkManager.IsListening &&
                          networkManager.IsConnectedClient,
                    $"Cycle {cycle}: Client B reconnect failed.");
                ApplyNetworkSimulation();
                await WaitForReadySceneAsync(ProjectSceneKind.Game);
                TrackRuntimeHighWatermarks();
                ReportCyclePhase(cycle, "rejoined-game");
                reconnects++;
            }

            await WaitForObjectiveProgressAsync(cycle);
            await WaitForMarkerAsync(cycle, "host", "shutdown-signal");

            NetworkShutdownResult shutdown =
                await session.ShutdownToMainMenuAsync();
            ValidateShutdownResult(shutdown, $"{roleName} cycle {cycle}");
            await WaitForCleanMainMenuAsync();
            ValidateCleanBaseline(cycle);
            ReportCyclePhase(cycle, "clean");
            await WaitForMarkerAsync(cycle, "host", "complete");

            CompleteClientCycle(fault);
            cycle++;
        }
    }

    private void FinishHostCycle(
        int cycle,
        NetworkSoakFault fault)
    {
        completedCycles++;
        completedFaults.Add(fault.ToString());
        ReportCyclePhase(cycle, "complete");
    }

    private void CompleteClientCycle(NetworkSoakFault fault)
    {
        completedCycles++;
        completedFaults.Add(fault.ToString());
    }

    private bool ShouldFinishSoak()
    {
        return completedCycles >= RequiredFaultCount &&
               Time.realtimeSinceStartup - startedAt >=
               requestedDurationSeconds;
    }

    private async Task RequestDisconnectAndRecoverAsync(int cycle)
    {
        disconnects++;
        ReportCyclePhase(cycle, "disconnect-request");
        await WaitForCleanMainMenuAsync();
        ValidateCleanBaseline(cycle);
        ReportCyclePhase(cycle, "disconnected");
    }

    private async Task DisconnectFaultClientAsync(int cycle)
    {
        await WaitForMarkerAsync(cycle, "client-b", "disconnect-request");

        ulong clientId = GetFaultClientId();
        networkManager.DisconnectClient(clientId);

        await WaitUntilAsync(
            () => !networkManager.ConnectedClients.ContainsKey(clientId),
            $"Cycle {cycle}: Host did not disconnect Client B.",
            false);
        disconnects++;
    }

    private void PrepareFault(NetworkSoakFault fault)
    {
        switch (fault)
        {
            case NetworkSoakFault.Objective:
                PrepareObjectiveFault();
                return;
            case NetworkSoakFault.Drag:
                PrepareDragFault();
                return;
            case NetworkSoakFault.EnemyAttack:
                PrepareEnemyAttackFault();
                return;
            case NetworkSoakFault.MapLoading:
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault), fault, null);
        }
    }

    private void PrepareObjectiveFault()
    {
        NetworkObjectiveFlow objective =
            FindFirstObjectByType<NetworkObjectiveFlow>();

        if (objective == null || !objective.HasActiveObjective)
        {
            throw new InvalidOperationException(
                "Objective disconnect fault requires an active production objective.");
        }

        string objectiveId =
            objective.CurrentObjective.ObjectiveId.ToString();

        if (!objective.ReportObjectiveProgressServerOnly(
                objectiveId,
                0.1f,
                networkManager.LocalClientId))
        {
            throw new InvalidOperationException(
                "Could not commit objective progress before disconnect.");
        }
    }

    private void PrepareDragFault()
    {
        if (DraggingStateField == null)
        {
            throw new MissingFieldException(
                nameof(DraggableObject),
                "netIsDragging");
        }

        ulong faultClientId = GetFaultClientId();
        DraggableObject[] items = FindObjectsByType<DraggableObject>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < items.Length; i++)
        {
            DraggableObject item = items[i];
            NetworkObject networkObject =
                item != null ? item.GetComponent<NetworkObject>() : null;

            if (networkObject == null || !networkObject.IsSpawned)
                continue;

            NetworkVariable<bool> dragging =
                DraggingStateField.GetValue(item) as NetworkVariable<bool>;

            if (dragging == null)
                continue;

            networkObject.ChangeOwnership(faultClientId);
            dragging.Value = true;
            preparedDragObjectId = networkObject.NetworkObjectId;
            return;
        }

        throw new InvalidOperationException(
            "Drag disconnect fault requires a spawned production DraggableObject.");
    }

    private void PrepareEnemyAttackFault()
    {
        ulong faultClientId = GetFaultClientId();

        if (!networkManager.ConnectedClients.TryGetValue(
                faultClientId,
                out NetworkClient client) ||
            client.PlayerObject == null)
        {
            throw new InvalidOperationException(
                "Enemy attack disconnect fault requires Client B PlayerObject.");
        }

        EnemyTarget target =
            client.PlayerObject.GetComponentInChildren<EnemyTarget>();
        NetworkEnemyController enemy =
            FindFirstObjectByType<NetworkEnemyController>();
        EnemyAttackController attack =
            enemy != null ? enemy.GetComponent<EnemyAttackController>() : null;

        if (target == null ||
            enemy == null ||
            attack == null ||
            enemy.Config == null)
        {
            throw new InvalidOperationException(
                "Enemy attack disconnect fault requires the production enemy and Client B target.");
        }

        Vector3 attackPosition =
            target.AimPosition - Vector3.forward * 0.25f;
        enemy.transform.position = attackPosition;
        attack.ResetCooldown();

        EnemyAttackResult result = attack.TryStartAttack(
            target,
            enemy.Config,
            attackPosition,
            enemy);

        if (!result.WasStarted)
        {
            throw new InvalidOperationException(
                $"Production enemy attack did not start: {result.Type}.");
        }

        attackedPlayerObjectId =
            client.PlayerObject.NetworkObjectId;
    }

    private void ValidateRemoteDisconnectCleanup(NetworkSoakFault fault)
    {
        IServiceResolver services =
            context.SessionOrchestrator.SessionServices;

        if (services == null ||
            !services.TryResolve(out IPlayerScopeRegistry playerScopes) ||
            playerScopes.Count != 2)
        {
            throw new InvalidOperationException(
                "Remote disconnect did not close exactly one Player scope.");
        }

        if (fault == NetworkSoakFault.Drag)
        {
            if (!networkManager.SpawnManager.SpawnedObjects.TryGetValue(
                    preparedDragObjectId,
                    out NetworkObject itemObject))
            {
                throw new InvalidOperationException(
                    "Dragged object was destroyed with its disconnected owner.");
            }

            DraggableObject item =
                itemObject.GetComponent<DraggableObject>();
            NetworkVariable<bool> dragging =
                DraggingStateField.GetValue(item) as NetworkVariable<bool>;
            Rigidbody body = itemObject.GetComponent<Rigidbody>();

            if (itemObject.OwnerClientId != NetworkManager.ServerClientId ||
                dragging == null ||
                dragging.Value ||
                body == null ||
                !body.useGravity)
            {
                throw new InvalidOperationException(
                    "Owner disconnect did not restore drag ownership, state and physics.");
            }
        }

        if (fault == NetworkSoakFault.EnemyAttack &&
            attackedPlayerObjectId != 0 &&
            networkManager.SpawnManager.SpawnedObjects.ContainsKey(
                attackedPlayerObjectId))
        {
            throw new InvalidOperationException(
                "Enemy target PlayerObject survived its owner's disconnect.");
        }
    }

    private ulong GetFaultClientId()
    {
        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (clientId != NetworkManager.ServerClientId &&
                networkManager.ConnectedClients.TryGetValue(
                    clientId,
                    out NetworkClient client) &&
                client.PlayerObject != null &&
                client.PlayerObject.OwnerClientId == clientId)
            {
                string marker = GetProtocolPath(
                    $"client-b.identity-{clientId}.ready");

                if (File.Exists(marker))
                    return clientId;
            }
        }

        // Identity marker is written after Client B learns its assigned id.
        string[] markers = Directory.GetFiles(
            runDirectory,
            "client-b.identity-*.ready");

        for (int i = 0; i < markers.Length; i++)
        {
            string name = Path.GetFileNameWithoutExtension(markers[i]);
            int start = name.LastIndexOf("identity-", StringComparison.Ordinal);

            if (start >= 0 &&
                ulong.TryParse(
                    name.Substring(start + "identity-".Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out ulong clientId) &&
                networkManager.ConnectedClients.ContainsKey(clientId))
            {
                return clientId;
            }
        }

        throw new InvalidOperationException(
            "Host could not identify Client B.");
    }

    private static void FreezeAutonomousEnemySimulation()
    {
        NetworkEnemyController[] enemies =
            FindObjectsByType<NetworkEnemyController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        if (enemies.Length == 0)
        {
            throw new InvalidOperationException(
                "Network soak requires at least one production enemy.");
        }

        for (int i = 0; i < enemies.Length; i++)
        {
            if (enemies[i] != null)
                enemies[i].enabled = false;
        }
    }

    private async Task CommitAndSynchronizeObjectiveProgressAsync(int cycle)
    {
        NetworkObjectiveFlow objective = null;
        await WaitUntilAsync(
            () =>
            {
                objective = FindFirstObjectByType<NetworkObjectiveFlow>();
                return objective != null && objective.HasActiveObjective;
            },
            $"Cycle {cycle}: production objective is unavailable.");

        string objectiveId =
            objective.CurrentObjective.ObjectiveId.ToString();
        float targetProgress = Mathf.Max(
            objective.CurrentObjective.Progress01,
            0.25f);

        if (!Mathf.Approximately(
                objective.CurrentObjective.Progress01,
                targetProgress) &&
            !objective.ReportObjectiveProgressServerOnly(
                objectiveId,
                targetProgress,
                networkManager.LocalClientId))
        {
            throw new InvalidOperationException(
                $"Cycle {cycle}: objective health update failed.");
        }

        ReportCyclePhase(cycle, "objective-progress");
        await WaitForMarkerAsync(cycle, "client-a", "objective-progress");
        await WaitForMarkerAsync(cycle, "client-b", "objective-progress");
    }

    private async Task WaitForObjectiveProgressAsync(int cycle)
    {
        NetworkObjectiveFlow objective = null;
        await WaitUntilAsync(
            () =>
            {
                objective = FindFirstObjectByType<NetworkObjectiveFlow>();
                return objective != null &&
                       objective.HasActiveObjective &&
                       objective.CurrentObjective.Progress01 >= 0.25f;
            },
            $"Cycle {cycle}: {roleName} did not synchronize objective health update.");
        ReportCyclePhase(cycle, "objective-progress");
    }

    private void ApplyNetworkSimulation()
    {
        if (networkManager.NetworkConfig.NetworkTransport is not UnityTransport transport)
        {
            throw new InvalidOperationException(
                "Network soak requires UnityTransport.");
        }

        ref NetworkDriver driver = ref transport.GetNetworkDriver();

        if (!driver.IsCreated)
        {
            throw new InvalidOperationException(
                "UnityTransport driver was not created before simulator setup.");
        }

        driver.ModifyNetworkSimulatorParameters(
            new NetworkSimulatorParameter
            {
                ReceivePacketLossPercent = packetLossPercent,
                SendPacketLossPercent = packetLossPercent,
                SendDelayMS = latencyMs,
                SendJitterMS = jitterMs
            });
    }

    private void CaptureCleanBaseline()
    {
        globalScope = context.Services as ServiceScope;

        if (globalScope == null)
        {
            throw new InvalidOperationException(
                "Production Global resolver is not backed by ServiceScope.");
        }

        baselineGlobalChildren = globalScope.ChildScopeCount;
        baselineGlobalServices = globalScope.LocalServiceCount;
        baselineGlobalRegistrationOrder =
            globalScope.RegistrationOrderCount;
        baselineSceneScopes = appRuntime.SceneScopeCount;
        baselineNetworkObjects = CountLiveNetworkObjects();
        ValidateCleanBaseline(-1);
    }

    private void ValidateCleanBaseline(int cycle)
    {
        string prefix = cycle < 0
            ? "Initial MainMenu"
            : $"Cycle {cycle}";

        if (!G.IsReady ||
            !context.IsReady ||
            context.SessionOrchestrator.SessionServices != null ||
            globalScope.IsDisposed ||
            globalScope.ChildScopeCount != baselineGlobalChildren ||
            globalScope.LocalServiceCount != baselineGlobalServices ||
            globalScope.RegistrationOrderCount !=
                baselineGlobalRegistrationOrder ||
            appRuntime.SceneScopeCount != baselineSceneScopes ||
            GetSpawnedObjectCount() != 0 ||
            CountLiveNetworkObjects() != baselineNetworkObjects ||
            DraggableObject.ActiveDraggedObjects.Count != 0)
        {
            throw new InvalidOperationException(
                $"{prefix}: Scene/Player/Session scope or NetworkObject leak detected. " +
                $"GlobalChildren={globalScope.ChildScopeCount}/{baselineGlobalChildren}, " +
                $"SceneScopes={appRuntime.SceneScopeCount}/{baselineSceneScopes}, " +
                $"Spawned={GetSpawnedObjectCount()}, " +
                $"NetworkObjects={CountLiveNetworkObjects()}/{baselineNetworkObjects}, " +
                $"ActiveDrags={DraggableObject.ActiveDraggedObjects.Count}.");
        }
    }

    private void TrackRuntimeHighWatermarks()
    {
        maxSpawnedObjects = Mathf.Max(
            maxSpawnedObjects,
            GetSpawnedObjectCount());
        maxSceneScopes = Mathf.Max(
            maxSceneScopes,
            appRuntime.SceneScopeCount);
    }

    private int GetSpawnedObjectCount()
    {
        return networkManager != null &&
               networkManager.SpawnManager != null
            ? networkManager.SpawnManager.SpawnedObjects.Count
            : 0;
    }

    private static int CountLiveNetworkObjects()
    {
        return FindObjectsByType<NetworkObject>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None).Length;
    }

    private async Task BindProductionRuntimeAsync()
    {
        await WaitUntilAsync(
            TryBindProductionRuntime,
            "A single production ProjectContext/AppRuntime was not created from Bootstrap.unity.",
            false);
    }

    private bool TryBindProductionRuntime()
    {
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

        if (foundSessionState == null ||
            foundContext.NetworkManager == null)
        {
            return false;
        }

        context = foundContext;
        appRuntime = foundRuntime;
        sessionState = foundSessionState;
        networkManager = foundContext.NetworkManager;
        return true;
    }

    private async Task WaitForMainMenuAsync()
    {
        await WaitUntilAsync(
            IsCleanMainMenu,
            "ProjectContext/AppRuntime did not publish G and commit MainMenu.",
            false);
    }

    private async Task WaitForCleanMainMenuAsync()
    {
        await WaitUntilAsync(
            IsCleanMainMenu,
            "Process did not finish NGO and scope cleanup before MainMenu.",
            false);
    }

    private bool IsCleanMainMenu()
    {
        return context != null &&
               appRuntime != null &&
               context.IsReady &&
               G.IsReady &&
               context.StateMachine != null &&
               context.StateMachine.CurrentState == GameState.MainMenu &&
               context.GetActiveSceneKind() == ProjectSceneKind.MainMenu &&
               sessionState.CurrentState == NetworkSessionState.Offline &&
               !networkManager.IsListening &&
               !networkManager.IsClient &&
               !networkManager.IsServer &&
               context.SessionOrchestrator.SessionServices == null &&
               G.TryResolve(out IProjectSceneFlowService flow) &&
               !flow.HasPendingOperation;
    }

    private async Task WaitForReadySceneAsync(ProjectSceneKind sceneKind)
    {
        await WaitUntilAsync(
            () => IsReadyScene(sceneKind),
            $"Production runtime did not commit ready scene '{sceneKind}'.");

        IServiceResolver services =
            context.SessionOrchestrator.SessionServices;

        if (!SessionServiceReadinessPolicy.Validate(
                sceneKind,
                services,
                out string error))
        {
            throw new InvalidOperationException(error);
        }

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

        GameState expectedGameState =
            sceneKind == ProjectSceneKind.Lobby
                ? GameState.Lobby
                : GameState.InGame;
        NetworkSessionState expectedSessionState =
            sceneKind == ProjectSceneKind.Lobby
                ? NetworkSessionState.Lobby
                : NetworkSessionState.InGame;
        IServiceResolver services =
            context.SessionOrchestrator.SessionServices;

        return context.StateMachine.CurrentState == expectedGameState &&
               sessionState.CurrentState == expectedSessionState &&
               services != null &&
               SessionServiceReadinessPolicy.Validate(
                   sceneKind,
                   services,
                   out _) &&
               SessionServiceReadinessPolicy.ValidateServerPhase(
                   sceneKind,
                   services,
                   out _);
    }

    private async Task<bool> WaitForNextCycleAsync(int cycle)
    {
        string nextCycle = GetProtocolPath(
            $"host.cycle-{cycle:D3}.network.ready");
        string completed = GetProtocolPath("soak.complete.signal");
        float deadline =
            Time.realtimeSinceStartup + stepTimeoutSeconds;

        while (!File.Exists(nextCycle))
        {
            if (File.Exists(completed))
                return false;

            if (Time.realtimeSinceStartup >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out waiting for cycle {cycle} or soak completion.");
            }

            await Task.Delay(25);
        }

        return true;
    }

    private async Task WaitForMarkerAsync(
        int cycle,
        string owner,
        string phase)
    {
        string path = GetProtocolPath(
            $"{owner}.cycle-{cycle:D3}.{phase}.ready");
        await WaitUntilAsync(
            () => File.Exists(path),
            $"Cycle {cycle}: timed out waiting for {owner}.{phase}.",
            false);
    }

    private async Task WaitUntilAsync(
        Func<bool> predicate,
        string failureMessage,
        bool failOnGameError = true)
    {
        float deadline =
            Time.realtimeSinceStartup + stepTimeoutSeconds;

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

    private void ReportCyclePhase(int cycle, string phase)
    {
        visibleCycle = cycle;
        visiblePhase = phase;

        if (role == NetworkSoakRole.ClientB &&
            phase == "lobby")
        {
            WriteAtomic(
                GetProtocolPath(
                    $"client-b.identity-{networkManager.LocalClientId}.ready"),
                DateTime.UtcNow.ToString("O"));
        }

        WriteAtomic(
            GetProtocolPath(
                $"{roleName}.cycle-{cycle:D3}.{phase}.ready"),
            DateTime.UtcNow.ToString("O"));
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

    private void Complete(
        bool succeeded,
        string message,
        Exception exception = null)
    {
        if (resultWritten)
            return;

        resultWritten = true;
        NetworkSoakRoleResult result = new()
        {
            role = roleName,
            succeeded = succeeded,
            message = message ?? string.Empty,
            exception = exception?.ToString() ?? string.Empty,
            unityVersion = Application.unityVersion,
            durationSeconds =
                Time.realtimeSinceStartup - startedAt,
            completedCycles = completedCycles,
            disconnects = disconnects,
            reconnects = reconnects,
            maxSpawnedObjects = maxSpawnedObjects,
            maxSceneScopes = maxSceneScopes,
            faults = completedFaults.ToArray()
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
        string temporaryPath =
            path + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, content);

        if (File.Exists(path))
            File.Delete(path);

        File.Move(temporaryPath, path);
    }

    private static bool TryReadRole(
        string[] arguments,
        out NetworkSoakRole parsedRole)
    {
        parsedRole = NetworkSoakRole.None;

        if (!TryReadArgument(
                arguments,
                RoleArgument,
                out string value))
        {
            return false;
        }

        if (string.Equals(
                value,
                "host",
                StringComparison.OrdinalIgnoreCase))
        {
            parsedRole = NetworkSoakRole.Host;
        }
        else if (string.Equals(
                     value,
                     "client-a",
                     StringComparison.OrdinalIgnoreCase))
        {
            parsedRole = NetworkSoakRole.ClientA;
        }
        else if (string.Equals(
                     value,
                     "client-b",
                     StringComparison.OrdinalIgnoreCase))
        {
            parsedRole = NetworkSoakRole.ClientB;
        }

        return parsedRole != NetworkSoakRole.None;
    }

    private static string GetRoleName(NetworkSoakRole parsedRole)
    {
        return parsedRole switch
        {
            NetworkSoakRole.Host => "host",
            NetworkSoakRole.ClientA => "client-a",
            NetworkSoakRole.ClientB => "client-b",
            _ => "unknown"
        };
    }

    private static float ReadFloat(
        string[] arguments,
        string name,
        float fallback,
        float minimum,
        float maximum)
    {
        if (TryReadArgument(arguments, name, out string value) &&
            float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsed))
        {
            return Mathf.Clamp(parsed, minimum, maximum);
        }

        return fallback;
    }

    private static uint ReadUInt(
        string[] arguments,
        string name,
        uint fallback,
        uint maximum)
    {
        if (TryReadArgument(arguments, name, out string value) &&
            uint.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out uint parsed))
        {
            return Math.Min(parsed, maximum);
        }

        return fallback;
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
