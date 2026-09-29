using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class PlayerEnemyAttackReceiverTests
{
    private sealed class MatchCompletionServiceStub : IMatchCompletionService
    {
        public bool IsMatchRunning { get; set; }
        public bool CompletionResult { get; set; }
        public int CompletionCount { get; private set; }
        public GameResultData LastResult { get; private set; }

        public bool CompleteMatchServerOnly(
            GameResultData matchResult,
            string reason)
        {
            CompletionCount++;
            LastResult = matchResult;
            return CompletionResult;
        }

        public GameResultData CurrentResult => GameResultData.None;
        public event System.Action<GameResultData> MatchResolved { add { } remove { } }
    }

    private PlayerEnemyAttackCompletionGate completionGate;
    private readonly List<GameObject> createdPlayers = new();

    [SetUp]
    public void SetUp()
    {
        completionGate = new PlayerEnemyAttackCompletionGate();
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = createdPlayers.Count - 1; i >= 0; i--)
        {
            if (createdPlayers[i] != null)
                Object.DestroyImmediate(createdPlayers[i]);
        }

        createdPlayers.Clear();
    }

    // Caught means hands off the controls, always; somebody to watch is extra,
    // and only when somebody is still playing - asked of the players, so a
    // multiplayer lobby started alone behaves like a singleplayer game. Once
    // the letting go lived inside the watching, and a caught player given
    // nobody to watch kept turning the camera of a body that was not there.
    [TestCase(true)]
    [TestCase(false)]
    public void CaughtPlayer_LetsGoOfTheControls_AndSpectatesOnlyWhenSomebodyIsStillPlaying(bool alone)
    {
        GameObject player = new("Caught player");
        createdPlayers.Add(player);

        PlayerEnemyAttackReceiver receiver = player.AddComponent<PlayerEnemyAttackReceiver>();
        CameraLook look = player.AddComponent<CameraLook>();
        PlayerController controller = player.AddComponent<PlayerController>();

        List<PlayerEnemyAttackReceiver> players = new() { receiver };

        if (!alone)
        {
            GameObject other = new("Still playing");
            createdPlayers.Add(other);
            players.Add(other.AddComponent<PlayerEnemyAttackReceiver>());
        }

        receiver.LeavePlayLocally(players);

        Assert.That(look.enabled, Is.False, "A caught player can still look round.");
        Assert.That(controller.enabled, Is.False, "A caught player can still move.");
        Assert.That(player.GetComponent<PlayerSpectatorView>() != null, Is.EqualTo(!alone),
            alone
                ? "A player caught alone was given somebody to watch."
                : "A caught player was given nobody to watch while somebody was still playing.");
    }

    [Test]
    public void PlayedAlone_IsAboutHowManyWereInTheMatch()
    {
        Assert.That(PlayerEnemyAttackReceiver.PlayedAlone(1), Is.True);
        Assert.That(PlayerEnemyAttackReceiver.PlayedAlone(2), Is.False);
    }

    // Being caught used to end the match for everyone. It takes one player out
    // now, and only the last one still playing loses it.
    [Test]
    public void MatchIsLostOnlyWhenNobodyIsLeftInPlay()
    {
        PlayerEnemyAttackReceiver first = CreatePlayer();
        PlayerEnemyAttackReceiver second = CreatePlayer();
        List<PlayerEnemyAttackReceiver> players = new() { first, second };

        Assert.That(PlayerEnemyAttackReceiver.HasPlayerInPlay(players), Is.True);

        Eliminate(first);
        Assert.That(
            PlayerEnemyAttackReceiver.HasPlayerInPlay(players),
            Is.True,
            "One caught player must not end the match for the survivors.");

        Eliminate(second);
        Assert.That(PlayerEnemyAttackReceiver.HasPlayerInPlay(players), Is.False);

        Assert.That(
            PlayerEnemyAttackReceiver.HasPlayerInPlay(
                new List<PlayerEnemyAttackReceiver> { null }),
            Is.False);
        Assert.That(PlayerEnemyAttackReceiver.HasPlayerInPlay(null), Is.False);
    }

    [Test]
    public void CaughtPlayerCannotHideAnyMore()
    {
        PlayerEnemyAttackReceiver player = CreatePlayer();

        Assert.That(player.CanEnterHiding, Is.True);

        Eliminate(player);

        Assert.That(player.CanEnterHiding, Is.False);
    }

    [Test]
    public void SpectatorCyclesThroughEveryoneStillPlayingAndWrapsRound()
    {
        PlayerEnemyAttackReceiver caught = CreatePlayer();
        PlayerEnemyAttackReceiver first = CreatePlayer();
        PlayerEnemyAttackReceiver second = CreatePlayer();
        PlayerEnemyAttackReceiver alsoCaught = CreatePlayer();
        List<PlayerEnemyAttackReceiver> players = new()
        {
            caught,
            first,
            second,
            alsoCaught
        };

        Eliminate(caught);
        Eliminate(alsoCaught);

        PlayerEnemyAttackReceiver watched =
            PlayerSpectatorView.NextTarget(players, caught, null);
        Assert.That(watched, Is.SameAs(first), "Watching starts with a survivor.");

        watched = PlayerSpectatorView.NextTarget(players, caught, watched);
        Assert.That(watched, Is.SameAs(second));

        watched = PlayerSpectatorView.NextTarget(players, caught, watched);
        Assert.That(
            watched,
            Is.SameAs(first),
            "The list wraps round instead of running out.");

        Eliminate(first);
        Eliminate(second);

        Assert.That(
            PlayerSpectatorView.NextTarget(players, caught, watched),
            Is.Null,
            "Nobody is left to watch once every survivor is caught.");
    }

    // The way back. A spectator who clicked past the person they wanted used to
    // have to go all the way round the room to reach them again.
    [Test]
    public void SpectatorWalksBackTheWayItCame()
    {
        PlayerEnemyAttackReceiver caught = CreatePlayer();
        PlayerEnemyAttackReceiver first = CreatePlayer();
        PlayerEnemyAttackReceiver second = CreatePlayer();
        PlayerEnemyAttackReceiver third = CreatePlayer();

        List<PlayerEnemyAttackReceiver> players = new()
        {
            caught,
            first,
            second,
            third
        };

        Eliminate(caught);

        PlayerEnemyAttackReceiver watched =
            PlayerSpectatorView.NextTarget(players, caught, null);
        Assert.That(watched, Is.SameAs(first));

        watched = PlayerSpectatorView.NextTarget(players, caught, watched);
        Assert.That(watched, Is.SameAs(second));

        watched = PlayerSpectatorView.PreviousTarget(players, caught, watched);
        Assert.That(watched, Is.SameAs(first), "Back is the step just taken, undone.");

        // Backwards past the front of the list wraps to the end rather than
        // walking off it, which is where a negative index would have gone.
        watched = PlayerSpectatorView.PreviousTarget(players, caught, watched);
        Assert.That(watched, Is.SameAs(third), "The list wraps going back as well.");

        // And it skips the caught the same way forwards does - there is no
        // route back through your own body.
        Eliminate(third);

        watched = PlayerSpectatorView.PreviousTarget(players, caught, first);
        Assert.That(watched, Is.SameAs(second));
    }

    [Test]
    public void SpectatorNeverWatchesItsOwnCaughtBody()
    {
        PlayerEnemyAttackReceiver caught = CreatePlayer();
        List<PlayerEnemyAttackReceiver> players = new() { caught };

        Eliminate(caught);

        Assert.That(
            PlayerSpectatorView.NextTarget(players, caught, null),
            Is.Null);
    }

    private PlayerEnemyAttackReceiver CreatePlayer()
    {
        GameObject playerObject = new($"Player {createdPlayers.Count}");
        createdPlayers.Add(playerObject);
        return playerObject.AddComponent<PlayerEnemyAttackReceiver>();
    }

    private static void Eliminate(PlayerEnemyAttackReceiver player)
    {
        TestReflection.SetField(player, "isEliminated", true);
    }

    [Test]
    public void AttackBeforePlaying_DoesNotConsumeFollowingValidHit()
    {
        MatchCompletionServiceStub service = new()
        {
            IsMatchRunning = false,
            CompletionResult = true
        };

        Assert.That(
            completionGate.TryComplete(
                service,
                GameResultType.Defeat,
                7,
                "enemy hit"),
            Is.False);
        Assert.That(service.CompletionCount, Is.Zero);

        service.IsMatchRunning = true;

        Assert.That(
            completionGate.TryComplete(
                service,
                GameResultType.Defeat,
                7,
                "enemy hit"),
            Is.True);
        Assert.That(service.CompletionCount, Is.EqualTo(1));
        Assert.That(completionGate.CanAttempt, Is.False);
        Assert.That(service.LastResult.Source, Is.EqualTo(MatchResultSource.PlayerCaught));
        Assert.That(service.LastResult.InstigatorClientId, Is.EqualTo(7));
    }

    [Test]
    public void MissingService_DoesNotConsumeFollowingValidHit()
    {
        MatchCompletionServiceStub service = new()
        {
            IsMatchRunning = true,
            CompletionResult = true
        };

        Assert.That(
            completionGate.TryComplete(
                null,
                GameResultType.Defeat,
                7,
                "enemy hit"),
            Is.False);
        Assert.That(
            completionGate.TryComplete(
                service,
                GameResultType.Defeat,
                7,
                "enemy hit"),
            Is.True);
        Assert.That(service.CompletionCount, Is.EqualTo(1));
        Assert.That(completionGate.CanAttempt, Is.False);
    }

    [Test]
    public void RepeatedAttack_CompletesMatchOnlyOnce()
    {
        MatchCompletionServiceStub service = new()
        {
            IsMatchRunning = true,
            CompletionResult = true
        };

        Assert.That(
            completionGate.TryComplete(
                service,
                GameResultType.Defeat,
                7,
                "enemy hit"),
            Is.True);
        Assert.That(
            completionGate.TryComplete(
                service,
                GameResultType.Defeat,
                7,
                "enemy hit"),
            Is.False);
        Assert.That(service.CompletionCount, Is.EqualTo(1));
    }

    [Test]
    public void RejectedCompletion_DoesNotConsumeFollowingValidHit()
    {
        MatchCompletionServiceStub service = new()
        {
            IsMatchRunning = true,
            CompletionResult = false
        };

        Assert.That(
            completionGate.TryComplete(
                service,
                GameResultType.Defeat,
                7,
                "enemy hit"),
            Is.False);
        Assert.That(service.CompletionCount, Is.EqualTo(1));

        service.CompletionResult = true;

        Assert.That(
            completionGate.TryComplete(
                service,
                GameResultType.Defeat,
                7,
                "enemy hit"),
            Is.True);
        Assert.That(service.CompletionCount, Is.EqualTo(2));
    }
}
