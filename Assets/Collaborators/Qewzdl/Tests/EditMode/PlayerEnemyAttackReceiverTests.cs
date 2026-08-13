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
