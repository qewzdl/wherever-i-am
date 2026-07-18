using NUnit.Framework;

public sealed class PlayerEnemyAttackReceiverTests
{
    private sealed class MatchCompletionServiceStub : IMatchCompletionService
    {
        public bool IsMatchRunning { get; set; }
        public bool CompletionResult { get; set; }
        public int CompletionCount { get; private set; }

        public bool CompleteMatchServerOnly(
            GameResultData matchResult,
            string reason)
        {
            CompletionCount++;
            return CompletionResult;
        }
    }

    private PlayerEnemyAttackCompletionGate completionGate;

    [SetUp]
    public void SetUp()
    {
        completionGate = new PlayerEnemyAttackCompletionGate();
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
                "enemy hit"),
            Is.False);
        Assert.That(service.CompletionCount, Is.Zero);

        service.IsMatchRunning = true;

        Assert.That(
            completionGate.TryComplete(
                service,
                GameResultType.Defeat,
                "enemy hit"),
            Is.True);
        Assert.That(service.CompletionCount, Is.EqualTo(1));
        Assert.That(completionGate.CanAttempt, Is.False);
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
                "enemy hit"),
            Is.False);
        Assert.That(
            completionGate.TryComplete(
                service,
                GameResultType.Defeat,
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
                "enemy hit"),
            Is.True);
        Assert.That(
            completionGate.TryComplete(
                service,
                GameResultType.Defeat,
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
                "enemy hit"),
            Is.False);
        Assert.That(service.CompletionCount, Is.EqualTo(1));

        service.CompletionResult = true;

        Assert.That(
            completionGate.TryComplete(
                service,
                GameResultType.Defeat,
                "enemy hit"),
            Is.True);
        Assert.That(service.CompletionCount, Is.EqualTo(2));
    }
}
