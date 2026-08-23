using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Hands back a task nobody completes, which is what a host or join looks like
// while it is still going.
internal sealed class PendingSessionServiceStub : INetworkSessionService
{
    private readonly TaskCompletionSource<bool> pending = new();

    private readonly TaskCompletionSource<NetworkShutdownResult> pendingShutdown = new();

    public int HostCallCount { get; private set; }
    public int JoinCallCount { get; private set; }
    public int ShutdownCallCount { get; private set; }

    public Task HostLanAsync()
    {
        HostCallCount++;
        return pending.Task;
    }

    public Task JoinLanAsync(string ip)
    {
        JoinCallCount++;
        return pending.Task;
    }

    internal void CompleteConnection()
    {
        pending.TrySetResult(true);
    }

    public void StartGame(int mapId)
    {
    }

    public void StartGame(int mapId, int difficultyId)
    {
    }

    public void ReturnToLobby()
    {
    }

    public void ShutdownToMainMenu()
    {
    }

    public Task<NetworkShutdownResult> ShutdownToMainMenuAsync()
    {
        ShutdownCallCount++;
        return pendingShutdown.Task;
    }
}

internal sealed class GameStateServiceStub : IGameStateService
{
    public GameState CurrentState { get; private set; }

    public event Action<GameState, GameState> StateChanged;

    internal GameStateServiceStub(GameState initialState)
    {
        CurrentState = initialState;
    }

    public void ChangeState(GameState newState)
    {
        if (CurrentState == newState)
            return;

        GameState previous = CurrentState;
        CurrentState = newState;
        StateChanged?.Invoke(previous, newState);
    }
}

[Category("Baseline")]
public sealed class MapLobbyAndGameplayLogicTests
{
    [Test]
    public void ConnectionApproval_AllowsConfiguredLobbyAndCommittedLateJoinOnly()
    {
        NetworkConnectionApprovalConfig config =
            ScriptableObject.CreateInstance<NetworkConnectionApprovalConfig>();

        try
        {
            TestReflection.SetField(
                config,
                "remoteClientAllowedState",
                GameState.Lobby);
            TestReflection.SetField(config, "allowInGameLateJoin", true);

            Assert.That(config.CanAcceptRemoteClient(GameState.Lobby), Is.True);
            Assert.That(config.CanAcceptRemoteClient(GameState.InGame), Is.True);
            Assert.That(config.CanAcceptRemoteClient(GameState.LoadingGame), Is.False);
            Assert.That(config.CanAcceptRemoteClient(GameState.Connecting), Is.False);
            Assert.That(config.CanAcceptRemoteClient(GameState.MainMenu), Is.False);

            TestReflection.SetField(config, "allowInGameLateJoin", false);
            Assert.That(config.CanAcceptRemoteClient(GameState.InGame), Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(config);
        }
    }

    [Test]
    public void ConnectionPayload_RoundTripsAndRejectsMalformedData()
    {
        string playerId = Guid.NewGuid().ToString("N");

        Assert.That(
            NetworkConnectionPayloadCodec.TryEncode(
                7,
                "1.4.2",
                playerId,
                out byte[] encoded,
                out string error),
            Is.True,
            error);
        Assert.That(
            NetworkConnectionPayloadCodec.TryDecode(
                encoded,
                out NetworkConnectionPayload decoded,
                out error),
            Is.True,
            error);
        Assert.That(decoded.ProtocolVersion, Is.EqualTo(7));
        Assert.That(decoded.BuildVersion, Is.EqualTo("1.4.2"));
        Assert.That(decoded.PlayerId, Is.EqualTo(playerId));

        Assert.That(
            NetworkConnectionPayloadCodec.TryDecode(
                Encoding.UTF8.GetBytes("{}"),
                out _,
                out error),
            Is.False);
        Assert.That(error, Does.Contain("schema"));
    }

    // The id used to come from a file in persistentDataPath, which the editor
    // and a build on one machine share. Both then sent the same id and the
    // second was refused as a duplicate, so hosting and joining on one PC -
    // the way anybody tests this - could not work.
    [Test]
    public void ClientIdentity_DiffersBetweenRunsOnTheSameMachine()
    {
        NetworkClientIdentityProvider first = new();
        NetworkClientIdentityProvider second = new();

        string firstId = first.GetOrCreatePlayerId();
        string secondId = second.GetOrCreatePlayerId();

        Assert.That(
            NetworkConnectionPayloadCodec.TryNormalizePlayerId(firstId, out _),
            Is.True);
        Assert.That(
            secondId,
            Is.Not.EqualTo(firstId),
            "Two games running on one machine would be refused as one player.");

        // Stable while the game runs, so a reconnect within the grace period
        // is recognised as the same player.
        Assert.That(first.GetOrCreatePlayerId(), Is.EqualTo(firstId));
    }

    [Test]
    public void Admission_LetsAPlayerReplaceTheirOwnConnectionThatIsAlreadyGone()
    {
        HashSet<ulong> connected = new() { 7 };
        NetworkSessionAdmissionRegistry registry = new(
            maxPlayers: 2,
            protocolVersion: 4,
            buildVersion: "0.1.0",
            reconnectGracePeriodSeconds: 20d,
            timeProvider: () => 0d,
            isClientStillConnected: connected.Contains);
        string playerId = Guid.NewGuid().ToString("N");
        NetworkConnectionPayload payload = new(4, "0.1.0", playerId);

        Assert.That(
            registry.TryAdmit(7, payload).Status,
            Is.EqualTo(NetworkAdmissionStatus.Accepted));

        // Still connected: a genuine second copy is refused.
        Assert.That(
            registry.TryAdmit(8, payload).Status,
            Is.EqualTo(NetworkAdmissionStatus.DuplicatePlayer));

        // The transport has since lost the old one, without the disconnect
        // callback having run yet.
        connected.Remove(7);

        Assert.That(
            registry.TryAdmit(8, payload).Status,
            Is.EqualTo(NetworkAdmissionStatus.Reconnected));
        Assert.That(registry.ActivePlayerCount, Is.EqualTo(1));
        Assert.That(registry.TryGetPlayerId(8, out string reclaimed), Is.True);
        Assert.That(reclaimed, Is.EqualTo(playerId));

        // The abandoned client id no longer stands for anybody.
        Assert.That(registry.TryGetPlayerId(7, out _), Is.False);
    }

    [Test]
    public void Admission_PrivateLobbyRefusesNewcomersButLetsAReconnectBackIn()
    {
        GameObject serviceObject = new("Approval service test");

        // Inactive, so Awake does not run before the references are in place.
        serviceObject.SetActive(false);

        NetworkConnectionConfig connectionConfig =
            ScriptableObject.CreateInstance<NetworkConnectionConfig>();
        NetworkConnectionApprovalConfig approvalConfig =
            ScriptableObject.CreateInstance<NetworkConnectionApprovalConfig>();
        LobbyConfig lobbyConfig = ScriptableObject.CreateInstance<LobbyConfig>();

        try
        {
            NetworkConnectionApprovalService service =
                serviceObject.AddComponent<NetworkConnectionApprovalService>();
            GameStateMachine stateMachine =
                serviceObject.AddComponent<GameStateMachine>();
            stateMachine.ChangeState(GameState.Lobby);

            TestReflection.SetField(
                approvalConfig,
                "remoteClientAllowedState",
                GameState.Lobby);
            TestReflection.SetField(service, "stateMachine", stateMachine);
            TestReflection.SetField(service, "connectionConfig", connectionConfig);
            TestReflection.SetField(service, "approvalConfig", approvalConfig);
            TestReflection.SetField(service, "lobbyConfig", lobbyConfig);

            string playerId = Guid.NewGuid().ToString("N");
            string strangerId = Guid.NewGuid().ToString("N");

            Assert.That(
                RequestApproval(service, connectionConfig, 5, playerId).Approved,
                Is.False,
                "A lobby nobody made public yet must not take walk-ins.");

            service.SetAcceptingNewPlayers(true);
            Assert.That(
                RequestApproval(service, connectionConfig, 5, playerId).Approved,
                Is.True);

            NetworkSessionAdmissionRegistry registry =
                TestReflection.GetField<NetworkSessionAdmissionRegistry>(
                    service,
                    "admissionRegistry");
            registry.RecordDisconnect(5, reserveSlot: true);
            service.SetAcceptingNewPlayers(false);

            // The seat is still theirs; going private behind their back
            // would punish a lost connection.
            Assert.That(
                RequestApproval(service, connectionConfig, 6, playerId).Approved,
                Is.True);

            Assert.That(
                RequestApproval(service, connectionConfig, 7, strangerId).Approved,
                Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(serviceObject);
            UnityEngine.Object.DestroyImmediate(connectionConfig);
            UnityEngine.Object.DestroyImmediate(approvalConfig);
            UnityEngine.Object.DestroyImmediate(lobbyConfig);
        }
    }

    private static NetworkManager.ConnectionApprovalResponse RequestApproval(
        NetworkConnectionApprovalService service,
        NetworkConnectionConfig connectionConfig,
        ulong clientId,
        string playerId)
    {
        Assert.That(
            NetworkConnectionPayloadCodec.TryEncode(
                connectionConfig.ProtocolVersion,
                Application.version,
                playerId,
                out byte[] payload,
                out string encodeError),
            Is.True,
            encodeError);

        NetworkManager.ConnectionApprovalRequest request = new()
        {
            ClientNetworkId = clientId,
            Payload = payload
        };
        NetworkManager.ConnectionApprovalResponse response = new();

        TestReflection.Invoke(
            service,
            "HandleConnectionApproval",
            request,
            response);

        return response;
    }

    [Test]
    public void PlayerName_IsStrippedOfMarkupAndCutToWhatTheWireCarries()
    {
        // TextMeshPro reads tags, so a name like this would rewrite the lobby
        // list and the chat for everybody in the room.
        Assert.That(
            NetworkConnectionPayloadCodec.NormalizePlayerName("<color=red>Vasya</color>"),
            Is.EqualTo("color=redVasya/color"));

        Assert.That(
            NetworkConnectionPayloadCodec.NormalizePlayerName("  Vasya\n\t "),
            Is.EqualTo("Vasya"));

        Assert.That(NetworkConnectionPayloadCodec.NormalizePlayerName(null), Is.Empty);
        Assert.That(NetworkConnectionPayloadCodec.NormalizePlayerName("   "), Is.Empty);

        // FixedString32Bytes carries 29 bytes, and Cyrillic costs two each, so
        // the cut has to be by bytes - assigning a longer string throws.
        string longCyrillic = new string('я', 40);
        string cut = NetworkConnectionPayloadCodec.NormalizePlayerName(longCyrillic);

        Assert.That(
            System.Text.Encoding.UTF8.GetByteCount(cut),
            Is.LessThanOrEqualTo(NetworkConnectionPayloadCodec.MaximumPlayerNameBytes));
        Assert.That(cut, Is.Not.Empty);
        Assert.DoesNotThrow(() => new Unity.Collections.FixedString32Bytes(cut));
    }

    [Test]
    public void ConnectionPayload_CarriesTheNameThroughAnEncodeAndDecode()
    {
        string playerId = Guid.NewGuid().ToString("N");

        Assert.That(
            NetworkConnectionPayloadCodec.TryEncode(
                7,
                "1.4.2",
                playerId,
                "  <b>Vasya</b>  ",
                out byte[] encoded,
                out string error),
            Is.True,
            error);
        Assert.That(
            NetworkConnectionPayloadCodec.TryDecode(
                encoded,
                out NetworkConnectionPayload decoded,
                out error),
            Is.True,
            error);
        Assert.That(decoded.PlayerName, Is.EqualTo("bVasya/b"));
        Assert.That(decoded.PlayerId, Is.EqualTo(playerId));
    }

    [Test]
    public void DisconnectReason_KeepsWhatTheHostSaidAndDropsTransportNoise()
    {
        Assert.That(
            NetworkDisconnectReason.UserFacing("The host removed you from the lobby."),
            Is.EqualTo("The host removed you from the lobby."));

        // Netcode's own note, which explains nothing to a player.
        Assert.That(
            NetworkDisconnectReason.UserFacing(
                "[Disconnect Event] Client-2 disconnected by server."),
            Is.Empty);

        Assert.That(NetworkDisconnectReason.UserFacing(null), Is.Empty);
        Assert.That(NetworkDisconnectReason.UserFacing("   "), Is.Empty);
    }

    [Test]
    public void Admission_KickedPlayerCannotComeBackOnTheirReconnectReservation()
    {
        NetworkSessionAdmissionRegistry registry = new(
            maxPlayers: 4,
            protocolVersion: 4,
            buildVersion: "0.1.0",
            reconnectGracePeriodSeconds: 20d,
            timeProvider: () => 0d);
        string playerId = Guid.NewGuid().ToString("N");
        NetworkConnectionPayload payload = new(4, "0.1.0", playerId);

        Assert.That(
            registry.TryAdmit(9, payload).Status,
            Is.EqualTo(NetworkAdmissionStatus.Accepted));
        Assert.That(registry.Kick(9), Is.True);

        // Their seat goes back to the room rather than being held for them.
        Assert.That(registry.ActivePlayerCount, Is.EqualTo(0));
        Assert.That(registry.ReservedPlayerCount, Is.EqualTo(0));
        Assert.That(registry.HasReconnectReservation(playerId), Is.False);

        Assert.That(
            registry.TryAdmit(10, payload).Status,
            Is.EqualTo(NetworkAdmissionStatus.Kicked),
            "A kick that ends when they press join again is not a kick.");

        // Nobody to throw out is not an error, just nothing to do.
        Assert.That(registry.Kick(11), Is.False);

        // Whoever announces the disconnect asks once, so the room hears
        // "removed" instead of "left" - and a later leave is still a leave.
        Assert.That(registry.WasKicked(9), Is.True);
        Assert.That(registry.WasKicked(9), Is.False);

        // A new session starts with nobody barred.
        registry.Reset();
        Assert.That(
            registry.TryAdmit(12, payload).Status,
            Is.EqualTo(NetworkAdmissionStatus.Accepted));
    }

    [Test]
    public void Admission_ReservesCapacityAndOnlyReclaimsMatchingPlayer()
    {
        double now = 0d;
        NetworkSessionAdmissionRegistry registry = new(
            maxPlayers: 2,
            protocolVersion: 4,
            buildVersion: "0.1.0",
            reconnectGracePeriodSeconds: 20d,
            timeProvider: () => now);
        string hostId = Guid.NewGuid().ToString("N");
        string reconnectingId = Guid.NewGuid().ToString("N");
        string waitingId = Guid.NewGuid().ToString("N");

        Assert.That(
            registry.TryAdmit(
                0,
                new NetworkConnectionPayload(4, "0.1.0", hostId)).Status,
            Is.EqualTo(NetworkAdmissionStatus.Accepted));
        Assert.That(
            registry.TryAdmit(
                1,
                new NetworkConnectionPayload(4, "0.1.0", reconnectingId)).Status,
            Is.EqualTo(NetworkAdmissionStatus.Accepted));
        Assert.That(
            registry.TryAdmit(
                2,
                new NetworkConnectionPayload(4, "0.1.0", waitingId)).Status,
            Is.EqualTo(NetworkAdmissionStatus.SessionFull));

        registry.RecordDisconnect(1, reserveSlot: true);

        Assert.That(registry.ActivePlayerCount, Is.EqualTo(1));
        Assert.That(registry.ReservedPlayerCount, Is.EqualTo(1));
        Assert.That(registry.HasReconnectReservation(reconnectingId), Is.True);
        Assert.That(
            registry.TryAdmit(
                2,
                new NetworkConnectionPayload(4, "0.1.0", waitingId)).Status,
            Is.EqualTo(NetworkAdmissionStatus.SessionFull));
        Assert.That(
            registry.TryAdmit(
                3,
                new NetworkConnectionPayload(4, "0.1.0", reconnectingId)).Status,
            Is.EqualTo(NetworkAdmissionStatus.Reconnected));
        Assert.That(registry.IsReconnect(3), Is.True);
        Assert.That(
            registry.TryAdmit(
                4,
                new NetworkConnectionPayload(4, "0.1.0", reconnectingId)).Status,
            Is.EqualTo(NetworkAdmissionStatus.DuplicatePlayer));

        registry.RecordDisconnect(3, reserveSlot: true);
        now = 21d;

        Assert.That(
            registry.TryAdmit(
                2,
                new NetworkConnectionPayload(4, "0.1.0", waitingId)).Status,
            Is.EqualTo(NetworkAdmissionStatus.Accepted));
        Assert.That(registry.ReservedPlayerCount, Is.Zero);
    }

    [Test]
    public void Admission_RejectsProtocolAndBuildMismatchBeforeCapacity()
    {
        NetworkSessionAdmissionRegistry registry = new(
            maxPlayers: 1,
            protocolVersion: 5,
            buildVersion: "2.0.0",
            reconnectGracePeriodSeconds: 10d,
            timeProvider: () => 0d);
        string playerId = Guid.NewGuid().ToString("N");

        Assert.That(
            registry.TryAdmit(
                1,
                new NetworkConnectionPayload(4, "2.0.0", playerId)).Status,
            Is.EqualTo(NetworkAdmissionStatus.ProtocolMismatch));
        Assert.That(
            registry.TryAdmit(
                1,
                new NetworkConnectionPayload(5, "2.0.1", playerId)).Status,
            Is.EqualTo(NetworkAdmissionStatus.BuildMismatch));
        Assert.That(registry.ActivePlayerCount, Is.Zero);
    }

    [Test]
    public void MapDefinition_MatchesSceneByCaseInsensitiveNameOrNormalizedPath()
    {
        GameMapDefinition map = ScriptableObject.CreateInstance<GameMapDefinition>();

        try
        {
            map.ConfigureEditor(
                7,
                "Test Map",
                "Map_Test",
                "Assets/Scenes/Map_Test.unity");

            Assert.That(map.IsConfigured(out string error), Is.True, error);
            Assert.That(map.MatchesScene("map_test", string.Empty), Is.True);
            Assert.That(
                map.MatchesScene(
                    string.Empty,
                    @"assets\scenes\map_test.unity"),
                Is.True);
            Assert.That(map.MatchesScene("Other", "Assets/Other.unity"), Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(map);
        }
    }

    [Test]
    public void MapCatalog_EditorMutationsKeepSortedUniqueAndValidDefault()
    {
        GameMapCatalog catalog = ScriptableObject.CreateInstance<GameMapCatalog>();
        GameMapDefinition second = CreateMap(2, "Second");
        GameMapDefinition first = CreateMap(1, "First");

        try
        {
            Assert.That(catalog.AddMapEditor(second), Is.True);
            Assert.That(catalog.AddMapEditor(first), Is.True);
            Assert.That(catalog.AddMapEditor(first), Is.False);

            Assert.That(catalog.Count, Is.EqualTo(2));
            Assert.That(catalog.GetMapAt(0), Is.SameAs(first));
            Assert.That(catalog.GetMapAt(1), Is.SameAs(second));
            Assert.That(catalog.DefaultMapId, Is.EqualTo(second.MapId));

            Assert.That(catalog.SetDefaultMapEditor(first.MapId), Is.True);
            Assert.That(catalog.RemoveMapEditor(first.MapId), Is.True);
            Assert.That(catalog.DefaultMapId, Is.EqualTo(second.MapId));
            Assert.That(catalog.IsValid(out string error), Is.True, error);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(catalog);
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
        }
    }

    [Test]
    public void LobbySettings_DefaultAndEqualityAreDeterministic()
    {
        LobbySettingsData first = LobbySettingsData.CreateDefault();
        LobbySettingsData second = LobbySettingsData.CreateDefault();

        Assert.That(first.Equals(second), Is.True);
        Assert.That(first.MinPlayersToStart, Is.EqualTo(1));
        Assert.That(first.MaxPlayers, Is.EqualTo(4));
        Assert.That(first.RequireAllPlayersReady, Is.True);
        Assert.That(first.GameModeId, Is.EqualTo(0));
        Assert.That(first.MapId, Is.EqualTo(0));

        second.MapId = 10;
        Assert.That(first.Equals(second), Is.False);
    }

    [Test]
    public void MainMenu_IgnoresClicksWhileAHostOrJoinIsStillRunning()
    {
        GameObject menuObject = new(nameof(MainMenuDocument));

        try
        {
            MainMenuDocument menu = menuObject.AddComponent<MainMenuDocument>();
            PendingSessionServiceStub session = new();
            menu.Construct(session, errorService: null, settingsScreen: null);

            menu.Host();
            menu.Host();

            Assert.That(menu.IsRequestInFlight, Is.True);
            Assert.That(session.HostCallCount, Is.EqualTo(1), "Hosting was requested twice.");

            // Joining while a host is still going would be the same session
            // started from both ends.
            menu.Join("127.0.0.1");

            Assert.That(session.JoinCallCount, Is.EqualTo(0), "Joining slipped past a running host.");

            // Disposing releases the menu, so a reopened one is not stuck.
            menu.Dispose();

            Assert.That(menu.IsRequestInFlight, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(menuObject);
        }
    }

    // Cancelling is the way out of a join that is going nowhere, and it is
    // itself an operation that takes time. Asking for it twice would start a
    // second shutdown over the first, and asking for it with nothing running
    // would tear down a session the player never started.
    [Test]
    public void MainMenu_CancelsAConnectionOnceAndOnlyWhileOneIsRunning()
    {
        GameObject menuObject = new(nameof(MainMenuDocument));

        try
        {
            MainMenuDocument menu = menuObject.AddComponent<MainMenuDocument>();
            PendingSessionServiceStub session = new();
            menu.Construct(session, errorService: null, settingsScreen: null);

            menu.CancelRequest();

            Assert.That(
                session.ShutdownCallCount,
                Is.Zero,
                "Cancelling with nothing in flight shut a session down.");

            menu.Host();
            menu.CancelRequest();
            menu.CancelRequest();

            Assert.That(
                session.ShutdownCallCount,
                Is.EqualTo(1),
                "The second cancel started another shutdown.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(menuObject);
        }
    }

    [Test]
    public async Task MainMenu_KeepsBusyStateUntilTheSessionLifecycleFinishes()
    {
        GameObject menuObject = new(nameof(MainMenuDocument));

        try
        {
            MainMenuDocument menu = menuObject.AddComponent<MainMenuDocument>();
            NetworkSessionStateMachine state =
                menuObject.AddComponent<NetworkSessionStateMachine>();
            PendingSessionServiceStub session = new();

            menu.Construct(
                session,
                errorService: null,
                settingsScreen: null,
                sessionReadService: state);

            menu.Host();
            state.TryChangeState(NetworkSessionState.StartingHost);
            state.TryChangeState(NetworkSessionState.LoadingLobby);
            session.CompleteConnection();
            await Task.Yield();

            Assert.That(
                menu.IsRequestInFlight,
                Is.True,
                "The command task hid Busy before Lobby finished loading.");

            state.TryChangeState(NetworkSessionState.Failed);

            Assert.That(menu.IsRequestInFlight, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(menuObject);
        }
    }

    [TestCase("127.0.0.1", true, "127.0.0.1")]
    [TestCase(" 192.168.1.25 ", true, "192.168.1.25")]
    [TestCase("", false, "")]
    [TestCase("192.168.1", false, "192.168.1")]
    [TestCase("256.1.1.1", false, "256.1.1.1")]
    [TestCase("0.0.0.0", false, "0.0.0.0")]
    [TestCase("255.255.255.255", false, "255.255.255.255")]
    public void LanAddressValidator_AgreesWithTheLanConnectionBoundary(
        string value,
        bool expectedValid,
        string expectedNormalized)
    {
        bool isValid = LanAddressValidator.TryNormalize(
            value,
            out string normalized);

        Assert.That(isValid, Is.EqualTo(expectedValid));
        Assert.That(normalized, Is.EqualTo(expectedNormalized));
    }

    [Test]
    public void ProjectSceneDefinition_MatchesNormalizedPathAndName()
    {
        ProjectSceneDefinition definition = new(
            ProjectSceneKind.Game,
            "Game",
            "Assets/Collaborators/Qewzdl/Scenes/Game.unity",
            GameState.InGame);

        Assert.That(definition.Matches("game", string.Empty), Is.True);
        Assert.That(
            definition.Matches(
                string.Empty,
                @"assets\collaborators\qewzdl\scenes\game.unity"),
            Is.True);
        Assert.That(definition.Matches("Lobby", "Assets/Lobby.unity"), Is.False);
    }

    [Test]
    public void PauseService_AllowsOnlyInGameAndResumesOnStateExit()
    {
        GameObject serviceObject = new("Pause service test");
        GamePauseService pause = serviceObject.AddComponent<GamePauseService>();
        GameStateServiceStub state = new(GameState.MainMenu);
        int eventCount = 0;
        bool lastPauseState = false;

        try
        {
            pause.PauseStateChanged += paused =>
            {
                eventCount++;
                lastPauseState = paused;
            };
            pause.Construct(state);

            pause.Pause();
            Assert.That(pause.IsPaused, Is.False);

            state.ChangeState(GameState.InGame);
            pause.Pause();
            Assert.That(pause.IsPaused, Is.True);
            Assert.That(lastPauseState, Is.True);

            state.ChangeState(GameState.LoadingGame);
            Assert.That(pause.IsPaused, Is.False);
            Assert.That(lastPauseState, Is.False);
            Assert.That(eventCount, Is.EqualTo(2));

            pause.Dispose();
            state.ChangeState(GameState.InGame);
            Assert.That(pause.IsPaused, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(serviceObject);
        }
    }

    // One client that never reported the map used to end the match for
    // everyone, the host included - and a player quitting during the load
    // reports the same way, so leaving was enough to do it. Whether the map is
    // loaded here is what decides it now.
    [Test]
    public void MapService_KeepsTheMatchWhenAClientNeverReportsTheMap()
    {
        GameObject serviceObject = new("Map service");
        GameObject rootObject = new("Map root");
        GameMapDefinition map = CreateMap(3, "Prototype");

        try
        {
            GameMapService service = serviceObject.AddComponent<GameMapService>();
            GameMapRoot mapRoot = rootObject.AddComponent<GameMapRoot>();
            bool? reportedSuccess = null;

            // No catalog and no such loaded scene, so the handler's own
            // caching leaves these alone - which is what stands in for a map
            // that loaded here perfectly well.
            TestReflection.SetField(service, "selectedMap", map);
            TestReflection.SetField(service, "activeMap", map);
            TestReflection.SetField(service, "activeMapRoot", mapRoot);
            TestReflection.SetField(
                service,
                "pendingCompletion",
                (Action<bool>)(success => reportedSuccess = success));

            TestReflection.Invoke(
                service,
                "HandleNetworkLoadEventCompleted",
                map.SceneName,
                LoadSceneMode.Additive,
                new List<ulong> { 0 },
                new List<ulong> { 7 });

            Assert.That(
                reportedSuccess,
                Is.EqualTo(true),
                "One client failing to load the map ended the match for " +
                "everyone.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(map);
            UnityEngine.Object.DestroyImmediate(rootObject);
            UnityEngine.Object.DestroyImmediate(serviceObject);
        }
    }

    // A cancelled load is one nobody waits for any more: its completion is
    // dropped rather than reported, and the load it was cancelled from still
    // finishes on Unity's side afterwards.
    [Test]
    public void MapService_CancelledLoadDropsItsCompletionAndIgnoresWhatFinishesLate()
    {
        GameObject serviceObject = new("Cancelled map service");
        GameObject rootObject = new("Cancelled map root");
        GameMapDefinition map = CreateMap(4, "Prototype");

        try
        {
            GameMapService service = serviceObject.AddComponent<GameMapService>();
            GameMapRoot mapRoot = rootObject.AddComponent<GameMapRoot>();
            int completionCount = 0;
            int mapReadyCount = 0;

            service.MapReady += () => mapReadyCount++;

            TestReflection.SetField(service, "selectedMap", map);
            TestReflection.SetField(service, "activeMap", map);
            TestReflection.SetField(service, "activeMapRoot", mapRoot);
            TestReflection.SetField(service, "readyForMatch", true);
            TestReflection.SetField(service, "localLoadRequested", true);
            TestReflection.SetField(
                service,
                "pendingCompletion",
                (Action<bool>)(_ => completionCount++));

            int cancelledOperationVersion =
                TestReflection.GetField<int>(service, "operationVersion");

            service.CancelPending(ProjectOperationCancelReason.SessionShutdown);

            Assert.That(
                completionCount,
                Is.Zero,
                "A cancelled map load must not report back to a caller that gave up.");
            Assert.That(service.IsReadyForMatch, Is.False);
            Assert.That(service.ActiveMap, Is.Null);
            Assert.That(service.ActiveMapRoot, Is.Null);

            TestReflection.Invoke(
                service,
                "HandleLocalLoadOperationCompleted",
                cancelledOperationVersion,
                map);

            Assert.That(
                completionCount,
                Is.Zero,
                "The cancelled load finished late and still reported in.");
            Assert.That(mapReadyCount, Is.Zero);
            Assert.That(service.IsReadyForMatch, Is.False);
            Assert.That(service.ActiveMap, Is.Null);

            service.CancelPending(ProjectOperationCancelReason.SessionShutdown);

            Assert.That(completionCount, Is.Zero);
            Assert.That(mapReadyCount, Is.Zero);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(map);
            UnityEngine.Object.DestroyImmediate(rootObject);
            UnityEngine.Object.DestroyImmediate(serviceObject);
        }
    }

    private static GameMapDefinition CreateMap(int id, string displayName)
    {
        GameMapDefinition map = ScriptableObject.CreateInstance<GameMapDefinition>();
        map.ConfigureEditor(
            id,
            displayName,
            $"Map_{id}",
            $"Assets/Scenes/Map_{id}.unity");
        return map;
    }
}
