using System;
using NUnit.Framework;
using UnityEngine;

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
