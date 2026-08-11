using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// Regression coverage for target-bound stealth state inputs, target switching
// and the server-wide work budgets used when many enemies are active.
[Category("Baseline")]
public sealed class EnemyTargetBoundGazeTests
{
    private readonly List<GameObject> spawned = new();

    [TearDown]
    public void TearDown()
    {
        LogAssert.ignoreFailingMessages = false;

        for (int i = 0; i < spawned.Count; i++)
        {
            GameObject instance = spawned[i];

            if (instance == null)
            {
                continue;
            }

            Object.DestroyImmediate(instance);
        }

        spawned.Clear();
    }

    [Test]
    public void TargetObservation_SurvivesLiveTargetLossWithoutAdoptingAnotherPlayer()
    {
        LogAssert.ignoreFailingMessages = true;
        GameObject player = new("Observed target");
        spawned.Add(player);

        EnemyTarget target = player.AddComponent<EnemyTarget>();
        EnemyTargetMemory memory = new();
        Vector3 observedPosition = new(20f, 0f, 5f);

        memory.SetTarget(target);
        memory.RememberObservation(
            target,
            observedPosition,
            Vector3.right,
            12f);

        memory.ForgetCurrentTargetButKeepLastKnownPosition();
        Object.DestroyImmediate(player);

        Assert.That(memory.CurrentTarget, Is.Null);
        Assert.That(memory.TryGetLastObservation(out EnemyTargetObservation observation), Is.True);
        Assert.That(observation.Target == null, Is.True);
        Assert.That(observation.Position, Is.EqualTo(observedPosition));
        Assert.That(observation.Forward, Is.EqualTo(Vector3.right));
    }

    [Test]
    public void TargetObservation_ClearAllEndsTheEngagementSnapshot()
    {
        LogAssert.ignoreFailingMessages = true;
        GameObject player = new("Finished target");
        spawned.Add(player);

        EnemyTarget target = player.AddComponent<EnemyTarget>();
        EnemyTargetMemory memory = new();
        memory.RememberObservation(target, Vector3.one, Vector3.forward, 2f);

        memory.ClearAll();

        Assert.That(memory.TryGetLastObservation(out _), Is.False);
    }

    [Test]
    public void TargetObservation_StoresAStablePlanarFacing()
    {
        LogAssert.ignoreFailingMessages = true;
        GameObject player = new("Target facing uphill");
        spawned.Add(player);

        EnemyTarget target = player.AddComponent<EnemyTarget>();
        EnemyTargetMemory memory = new();
        memory.RememberObservation(
            target,
            Vector3.zero,
            new Vector3(2f, 10f, 0f),
            3f);

        Assert.That(memory.LastObservation.Forward, Is.EqualTo(Vector3.right));
    }

    // A retreat that gives up before sight can break would never reach its own
    // successful outcome, so the authored value is floored above it.
    [Test]
    public void RetreatTimeout_OutlastsBrokenSightDuration()
    {
        EnemyVisionConfig vision = ScriptableObject.CreateInstance<EnemyVisionConfig>();
        vision.retreatBrokenSightDuration = 5f;
        vision.retreatTimeout = 1f;

        EnemyConfig config = ScriptableObject.CreateInstance<EnemyConfig>();
        TestReflection.SetField(config, "visionProfile", vision);

        Assert.That(config.retreatTimeout, Is.GreaterThan(config.retreatBrokenSightDuration));

        vision.retreatTimeout = 30f;
        Assert.That(config.retreatTimeout, Is.EqualTo(30f));

        Object.DestroyImmediate(config);
        Object.DestroyImmediate(vision);
    }

    [Test]
    public void PerceptionScheduler_SharesOnePathQueryBudgetAcrossEveryEnemy()
    {
        EnemyServerPerceptionScheduler.ResetForTests();

        int budget = EnemyServerPerceptionScheduler.PathQueriesRemainingThisFrame;
        Assert.That(budget, Is.GreaterThan(0));

        Assert.That(
            EnemyServerPerceptionScheduler.TryReservePathQueries(
                enemyId: 1,
                requestedQueries: budget,
                out int granted),
            Is.True);
        Assert.That(granted, Is.EqualTo(budget));

        // The point of moving the budget off the individual enemy: once the
        // frame's allowance is gone it is gone for everyone, however many
        // enemies decided to repath at once.
        Assert.That(
            EnemyServerPerceptionScheduler.TryReservePathQueries(
                enemyId: 2,
                requestedQueries: 1,
                out _),
            Is.False);
        Assert.That(
            EnemyServerPerceptionScheduler.PathQueriesRemainingThisFrame,
            Is.Zero);

        EnemyServerPerceptionScheduler.ResetForTests();
    }

    [Test]
    public void PerceptionScheduler_ReusesAGazeAnswerUntilTheEnemyHasMoved()
    {
        EnemyServerPerceptionScheduler.ResetForTests();

        // Nobody is registered as looking, so the underlying answer is false;
        // what is being pinned here is that a far-away sample is treated as a
        // different question rather than served from the same entry.
        Assert.That(
            EnemyServerPerceptionScheduler.IsBodySeenByAnyone(1, Vector3.zero, 1.8f),
            Is.False);

        Assert.That(
            EnemyServerPerceptionScheduler.IsBodySeenByAnyone(
                1,
                new Vector3(50f, 0f, 0f),
                1.8f),
            Is.False);

        EnemyServerPerceptionScheduler.Forget(1);
        EnemyServerPerceptionScheduler.ResetForTests();
    }

    [Test]
    public void TargetSelector_HoldsItsTargetUntilAChallengerIsClearlyBetter()
    {
        LogAssert.ignoreFailingMessages = true;

        GameObject first = new("Target A");
        GameObject second = new("Target B");
        spawned.Add(first);
        spawned.Add(second);

        EnemyTarget held = first.AddComponent<EnemyTarget>();
        EnemyTarget challenger = second.AddComponent<EnemyTarget>();

        EnemyTargetSelector selector = new();

        // Nothing held yet, so anything visible is an improvement.
        Assert.That(
            Switch(selector, null, held, 1f, 0f, false, EnemyState.Chase, 0f),
            Is.True);

        selector.NotifyCommitted(held, 0f);

        // Inside the hold window nothing displaces it, however good.
        Assert.That(
            Switch(selector, held, challenger, 100f, 1f, true, EnemyState.Chase, 1f),
            Is.False);

        // Past it, a marginal improvement still is not worth the swap.
        Assert.That(
            Switch(selector, held, challenger, 1.2f, 1f, true, EnemyState.Chase, 5f),
            Is.False);

        Assert.That(
            Switch(selector, held, challenger, 2f, 1f, true, EnemyState.Chase, 5f),
            Is.True);

        // A target that cannot be seen has no score to defend with.
        Assert.That(
            Switch(selector, held, challenger, 0.1f, 0f, false, EnemyState.Chase, 5f),
            Is.True);
    }

    [Test]
    public void TargetSelector_RefusesToSwapMidAttackApproachOrAmbush()
    {
        LogAssert.ignoreFailingMessages = true;

        GameObject first = new("Locked target");
        GameObject second = new("Closer target");
        spawned.Add(first);
        spawned.Add(second);

        EnemyTarget held = first.AddComponent<EnemyTarget>();
        EnemyTarget challenger = second.AddComponent<EnemyTarget>();

        EnemyTargetSelector selector = new();
        selector.NotifyCommitted(held, 0f);

        EnemyState[] locked =
        {
            EnemyState.Attack,
            EnemyState.Flank,
            EnemyState.Ambush,
        };

        for (int i = 0; i < locked.Length; i++)
        {
            Assert.That(
                Switch(selector, held, challenger, 100f, 1f, true, locked[i], 60f),
                Is.False,
                $"{locked[i]} has already built a plan around its target.");
        }

        // Chase has not, so the same challenger wins there.
        Assert.That(
            Switch(selector, held, challenger, 100f, 1f, true, EnemyState.Chase, 60f),
            Is.True);
    }

    [Test]
    public void TargetSelector_DisabledSwitchingRejectsEveryDifferentVisibleTarget()
    {
        LogAssert.ignoreFailingMessages = true;

        GameObject first = new("Held target");
        GameObject second = new("Visible challenger");
        spawned.Add(first);
        spawned.Add(second);

        EnemyTarget held = first.AddComponent<EnemyTarget>();
        EnemyTarget challenger = second.AddComponent<EnemyTarget>();
        EnemyTargetSelector selector = new();
        selector.NotifyCommitted(held, 0f);

        Assert.That(
            selector.ShouldSwitchTo(
                held,
                challenger,
                candidateScore: 100f,
                currentScore: 0f,
                hasCurrentScore: false,
                currentState: EnemyState.Chase,
                serverTime: 60f,
                allowSwitchToNewVisibleTarget: false,
                minimumHoldDuration: 0f,
                requiredScoreAdvantage: 1f),
            Is.False);
    }

    private static bool Switch(
        EnemyTargetSelector selector,
        EnemyTarget current,
        EnemyTarget candidate,
        float candidateScore,
        float currentScore,
        bool hasCurrentScore,
        EnemyState state,
        float serverTime
    )
    {
        return selector.ShouldSwitchTo(
            current,
            candidate,
            candidateScore,
            currentScore,
            hasCurrentScore,
            state,
            serverTime,
            allowSwitchToNewVisibleTarget: true,
            minimumHoldDuration: 2f,
            requiredScoreAdvantage: 1.5f);
    }

}
