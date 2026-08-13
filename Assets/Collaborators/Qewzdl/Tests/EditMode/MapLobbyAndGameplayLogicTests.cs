using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

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
