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

    public void ShutdownToMainMenu()
    {
    }

    public Task<NetworkShutdownResult> ShutdownToMainMenuAsync()
    {
        return Task.FromResult(NetworkShutdownResult.Success());
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

        LobbyOwnershipService ownership = new(state);
        LobbyPlayerRegistry registry = new(state, ownership);

        Assert.That(registry.TryAddPlayer(10), Is.True);
        Assert.That(registry.TryAddPlayer(20), Is.True);
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

    private LobbyState CreateLobbyState()
    {
        GameObject stateObject = new("Lobby state test");
        createdObjects.Add(stateObject);
        return stateObject.AddComponent<LobbyState>();
    }
}
