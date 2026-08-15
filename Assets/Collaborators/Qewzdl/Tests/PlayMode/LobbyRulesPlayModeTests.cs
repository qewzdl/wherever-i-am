using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

internal sealed class LobbySessionServiceProbe : INetworkSessionService
{
    internal int StartGameCount { get; private set; }
    internal int LastMapId { get; private set; } = -1;
    internal int LastDifficultyId { get; private set; } = -1;
    internal int ReturnToLobbyCount { get; private set; }

    public Task HostLanAsync()
    {
        return Task.CompletedTask;
    }

    public Task JoinLanAsync(string ip)
    {
        return Task.CompletedTask;
    }

    public void StartGame(int mapId)
    {
        StartGameCount++;
        LastMapId = mapId;
    }

    public void StartGame(int mapId, int difficultyId)
    {
        StartGame(mapId);
        LastDifficultyId = difficultyId;
    }

    public void ReturnToLobby()
    {
        ReturnToLobbyCount++;
    }

    public void ShutdownToMainMenu()
    {
    }

    public Task<NetworkShutdownResult> ShutdownToMainMenuAsync()
    {
        return Task.FromResult(NetworkShutdownResult.Success());
    }
}

internal sealed class LobbyAdmissionServiceProbe : INetworkSessionAdmissionService
{
    private readonly Dictionary<ulong, string> playerIds = new();
    private readonly HashSet<ulong> reconnectingClients = new();
    private readonly HashSet<string> reservations = new();
    private readonly HashSet<ulong> deniedClients = new();

    internal void Deny(ulong clientId)
    {
        deniedClients.Add(clientId);
    }

    public bool TryGetPlayerId(ulong clientId, out string playerId)
    {
        if (deniedClients.Contains(clientId))
        {
            playerId = string.Empty;
            return false;
        }

        if (playerIds.TryGetValue(clientId, out playerId))
            return true;

        playerId = $"test-player-{clientId}";
        playerIds.Add(clientId, playerId);
        return true;
    }

    public bool IsReconnect(ulong clientId)
    {
        return reconnectingClients.Contains(clientId);
    }

    public bool HasReconnectReservation(string playerId)
    {
        return reservations.Contains(playerId);
    }

    public void RecordDisconnect(ulong clientId)
    {
        if (TryGetPlayerId(clientId, out string playerId))
            reservations.Add(playerId);
    }

    internal void ReconnectAs(ulong previousClientId, ulong newClientId)
    {
        TryGetPlayerId(previousClientId, out string playerId);
        playerIds[newClientId] = playerId;
        reconnectingClients.Add(newClientId);
        reservations.Remove(playerId);
    }
}

[Category("Baseline")]
public sealed class LobbyRulesPlayModeTests
{
    private readonly List<GameObject> createdObjects = new();

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        for (int i = 0; i < createdObjects.Count; i++)
        {
            if (createdObjects[i] != null)
                Object.Destroy(createdObjects[i]);
        }

        createdObjects.Clear();
        yield return null;
    }

    [UnityTest]
    public IEnumerator StartRules_RequireOpenLobbyMinimumPlayersAndReadiness()
    {
        LobbyState state = CreateLobbyState();
        yield return null;

        state.Settings.Value = new LobbySettingsData(
            minPlayersToStart: 2,
            maxPlayers: 4,
            requireAllPlayersReady: true,
            gameModeId: 0,
            mapId: 7);
        state.Phase.Value = LobbyPhase.Open;
        state.Players.Add(new LobbyPlayerData(1, "One", true));
        state.Players.Add(new LobbyPlayerData(2, "Two", false));

        LobbyStartRules rules = new();

        Assert.That(rules.CanStart(state), Is.False);

        LobbyPlayerData second = state.Players[1];
        second.IsReady = true;
        state.Players[1] = second;

        Assert.That(rules.CanStart(state), Is.True);

        state.Phase.Value = LobbyPhase.Starting;
        Assert.That(rules.CanStart(state), Is.False);
    }

    [UnityTest]
    public IEnumerator PlayerRegistry_AssignsAndTransfersRoomOwnership()
    {
        LobbyState state = CreateLobbyState();
        yield return null;

        state.Settings.Value = new LobbySettingsData(
            minPlayersToStart: 1,
            maxPlayers: 2,
            requireAllPlayersReady: false,
            gameModeId: 0,
            mapId: 0);
        state.Phase.Value = LobbyPhase.Open;

        LobbyAdmissionServiceProbe admission = new();
        LobbyOwnershipService ownership = new(state);
        LobbyPlayerRegistry registry = new(state, ownership, admission);

        Assert.That(registry.TryAddPlayer(10), Is.True);
        Assert.That(registry.TryAddPlayer(20), Is.True);

        // Capacity is admission's job now - it turns a client away before the
        // connection exists, where this could only turn it away afterwards.
        // What is left here is that an unadmitted client never reaches the
        // lobby list.
        admission.Deny(30);
        LogAssert.Expect(
            LogType.Warning,
            "Rejected lobby registration for unadmitted client '30'.");
        Assert.That(registry.TryAddPlayer(30), Is.False);

        Assert.That(state.Players.Count, Is.EqualTo(2));
        Assert.That(ownership.IsRoomOwner(10), Is.True);

        registry.RemovePlayer(10);

        Assert.That(state.Players.Count, Is.EqualTo(1));
        Assert.That(ownership.IsRoomOwner(20), Is.True);

        registry.RemovePlayer(20);

        Assert.That(state.Players.Count, Is.Zero);
        Assert.That(
            state.RoomOwnerClientId.Value,
            Is.EqualTo(LobbyState.NoRoomOwner));
    }

    [UnityTest]
    public IEnumerator PlayerRegistry_RestoresLobbyStateForApprovedReconnect()
    {
        LobbyState state = CreateLobbyState();
        yield return null;

        state.Settings.Value = new LobbySettingsData(
            minPlayersToStart: 1,
            maxPlayers: 1,
            requireAllPlayersReady: true,
            gameModeId: 0,
            mapId: 0);
        state.Phase.Value = LobbyPhase.Open;

        LobbyAdmissionServiceProbe admission = new();
        LobbyOwnershipService ownership = new(state);
        LobbyPlayerRegistry registry = new(state, ownership, admission);

        Assert.That(registry.TryAddPlayer(10), Is.True);
        LobbyPlayerData original = state.Players[0];
        original.PlayerName = "Persistent player";
        original.IsReady = true;
        state.Players[0] = original;

        admission.RecordDisconnect(10);
        registry.RemovePlayer(10);
        admission.ReconnectAs(10, 42);

        Assert.That(registry.TryAddPlayer(42), Is.True);
        Assert.That(state.Players.Count, Is.EqualTo(1));
        Assert.That(state.Players[0].ClientId, Is.EqualTo(42));
        Assert.That(
            state.Players[0].PlayerName.ToString(),
            Is.EqualTo("Persistent player"));
        Assert.That(state.Players[0].IsReady, Is.True);
    }

    [UnityTest]
    public IEnumerator StartService_CommitsStartingPhaseAndSelectedMapOnceRulesPass()
    {
        LobbyState state = CreateLobbyState();
        yield return null;

        state.Settings.Value = new LobbySettingsData(
            minPlayersToStart: 1,
            maxPlayers: 4,
            requireAllPlayersReady: true,
            gameModeId: 0,
            mapId: 12);
        state.Phase.Value = LobbyPhase.Open;
        state.Players.Add(new LobbyPlayerData(1, "Owner", true));

        LobbySessionServiceProbe session = new();
        LobbyStartService startService = new(state, new LobbyStartRules(), session);

        startService.TryStartGame();

        Assert.That(session.StartGameCount, Is.EqualTo(1));
        Assert.That(session.LastMapId, Is.EqualTo(12));
        Assert.That(state.Phase.Value, Is.EqualTo(LobbyPhase.Starting));
        Assert.That(state.CanStartGame.Value, Is.False);

        startService.TryStartGame();
        Assert.That(session.StartGameCount, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator SettingsAndCustomization_RejectInvalidMutationsAndCommitValidOnes()
    {
        LobbyState state = CreateLobbyState();
        LobbyConfig config = ScriptableObject.CreateInstance<LobbyConfig>();
        GameMapCatalog catalog = ScriptableObject.CreateInstance<GameMapCatalog>();
        GameMapDefinition map = ScriptableObject.CreateInstance<GameMapDefinition>();
        EnemyDifficultyCatalog difficulties =
            ScriptableObject.CreateInstance<EnemyDifficultyCatalog>();
        EnemyConfig easyEnemy = ScriptableObject.CreateInstance<EnemyConfig>();
        EnemyConfig hardEnemy = ScriptableObject.CreateInstance<EnemyConfig>();

        try
        {
            PlayModeTestReflection.SetField(map, "mapId", 7);
            PlayModeTestReflection.SetField(map, "displayName", "Test map");
            PlayModeTestReflection.SetField(map, "sceneName", "Game");
            PlayModeTestReflection.SetField(
                map,
                "scenePath",
                "Assets/Scenes/Game.unity");
            PlayModeTestReflection.SetField(catalog, "defaultMapId", 7);
            PlayModeTestReflection.SetField(
                catalog,
                "maps",
                new[] { map });
            PlayModeTestReflection.SetField(
                config,
                "gameModeIds",
                new[] { 2, 3 });
            PlayModeTestReflection.SetField(config, "mapCatalog", catalog);
            PlayModeTestReflection.SetField(
                difficulties,
                "difficulties",
                new[]
                {
                    new EnemyDifficultyCatalog.EnemyDifficultyEntry(0, "Test easy", easyEnemy),
                    new EnemyDifficultyCatalog.EnemyDifficultyEntry(4, "Test hard", hardEnemy)
                });
            PlayModeTestReflection.SetField(difficulties, "defaultDifficultyId", 4);
            PlayModeTestReflection.SetField(config, "difficultyCatalog", difficulties);

            yield return null;

            LobbySettingsService settings = new(state, config);
            settings.InitializeFromConfig();
            Assert.That(state.Phase.Value, Is.EqualTo(LobbyPhase.Open));
            Assert.That(state.Settings.Value.GameModeId, Is.EqualTo(2));
            Assert.That(state.Settings.Value.MapId, Is.EqualTo(7));
            Assert.That(state.Settings.Value.DifficultyId, Is.EqualTo(4));

            LogAssert.Expect(
                LogType.Warning,
                "Rejected invalid lobby difficulty id: 99.");
            settings.SetDifficulty(99);
            Assert.That(state.Settings.Value.DifficultyId, Is.EqualTo(4));

            settings.SetDifficulty(0);
            Assert.That(state.Settings.Value.DifficultyId, Is.EqualTo(0));

            Assert.That(difficulties.TryGetConfig(0, out EnemyConfig selected), Is.True);
            Assert.That(selected, Is.SameAs(easyEnemy));

            LogAssert.Expect(
                LogType.Warning,
                "Rejected invalid lobby game mode id: 99.");
            settings.SetGameMode(99);
            Assert.That(state.Settings.Value.GameModeId, Is.EqualTo(2));

            settings.SetGameMode(3);
            Assert.That(state.Settings.Value.GameModeId, Is.EqualTo(3));

            LobbyOwnershipService ownership = new(state);
            LobbyPlayerRegistry registry = new(
                state,
                ownership,
                new LobbyAdmissionServiceProbe());
            registry.TryAddPlayer(10);
            LobbyPlayerCustomizationService customization = new(state);
            customization.SetReady(10, true);
            customization.SetReady(999, true);

            Assert.That(state.Players[0].IsReady, Is.True);
            Assert.That(state.Players.Count, Is.EqualTo(1));

            state.Phase.Value = LobbyPhase.Starting;
            LogAssert.Expect(
                LogType.Warning,
                "Lobby settings cannot be changed while lobby phase is Starting.");
            settings.SetMap(7);
            Assert.That(state.Settings.Value.MapId, Is.EqualTo(7));
        }
        finally
        {
            Object.Destroy(config);
            Object.Destroy(catalog);
            Object.Destroy(map);
            Object.Destroy(difficulties);
            Object.Destroy(easyEnemy);
            Object.Destroy(hardEnemy);
        }
    }

    [UnityTest]
    public IEnumerator StartService_MissingSessionFailsClosedWithoutPhaseCommit()
    {
        LobbyState state = CreateLobbyState();
        yield return null;

        state.Settings.Value = new LobbySettingsData(
            minPlayersToStart: 1,
            maxPlayers: 4,
            requireAllPlayersReady: false,
            gameModeId: 0,
            mapId: 3);
        state.Phase.Value = LobbyPhase.Open;
        state.Players.Add(new LobbyPlayerData(1, "Owner", false));
        LobbyStartService service =
            new(state, new LobbyStartRules(), sessionService: null);

        LogAssert.Expect(LogType.Error, "Network session service is missing.");
        service.TryStartGame();

        Assert.That(state.Phase.Value, Is.EqualTo(LobbyPhase.Open));
        Assert.That(state.CanStartGame.Value, Is.True);
    }

    private LobbyState CreateLobbyState()
    {
        GameObject stateObject = new("Lobby state test");
        createdObjects.Add(stateObject);
        return stateObject.AddComponent<LobbyState>();
    }
}
