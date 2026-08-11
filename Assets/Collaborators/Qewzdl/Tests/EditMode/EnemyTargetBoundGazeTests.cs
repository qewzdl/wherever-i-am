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
        EnemyStealthTacticsConfig tactics =
            ScriptableObject.CreateInstance<EnemyStealthTacticsConfig>();
        tactics.retreatBrokenSightDuration = 5f;
        tactics.retreatTimeout = 1f;

        EnemyConfig config = ScriptableObject.CreateInstance<EnemyConfig>();
        TestReflection.SetField(config, "stealthTacticsProfile", tactics);

        Assert.That(config.retreatTimeout, Is.GreaterThan(config.retreatBrokenSightDuration));

        tactics.retreatTimeout = 30f;
        Assert.That(config.retreatTimeout, Is.EqualTo(30f));

        Object.DestroyImmediate(config);
        Object.DestroyImmediate(tactics);
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

    // The hole the lock had: perception nulls the held target the moment it
    // stops being valid, and "nothing to defend, take anything visible" was
    // checked before the lock was. So a target despawning mid-flank handed the
    // approach - and the ambush at the end of it - to whoever else was in
    // view, with a plan built for somebody who had left the level.
    [Test]
    public void TargetSelector_RefusesAChallengerWhenTheLockedTargetIsGone()
    {
        LogAssert.ignoreFailingMessages = true;

        GameObject first = new("Despawned target");
        GameObject second = new("Visible challenger");
        spawned.Add(first);
        spawned.Add(second);

        EnemyTarget held = first.AddComponent<EnemyTarget>();
        EnemyTarget challenger = second.AddComponent<EnemyTarget>();

        EnemyTargetSelector selector = new();
        selector.NotifyCommitted(held, 0f);

        Object.DestroyImmediate(first);

        EnemyState[] locked =
        {
            EnemyState.Attack,
            EnemyState.Stalk,
            EnemyState.Retreat,
            EnemyState.Flank,
            EnemyState.Ambush,
        };

        for (int i = 0; i < locked.Length; i++)
        {
            Assert.That(
                Switch(selector, null, challenger, 100f, 0f, false, locked[i], 60f),
                Is.False,
                $"{locked[i]} committed to somebody else and has not aborted.");
        }

        // Chase has built nothing around its target, so a vanished one leaves
        // it free to take the person in front of it.
        Assert.That(
            Switch(selector, null, challenger, 100f, 0f, false, EnemyState.Chase, 60f),
            Is.True);

        // Leaving the manoeuvre is the explicit abort, and it is the only
        // thing that releases the lock.
        selector.Clear();

        Assert.That(
            Switch(selector, null, challenger, 100f, 0f, false, EnemyState.Flank, 60f),
            Is.True);
    }

    [Test]
    public void StealthManeuver_HoldsOnePersonAcrossEveryPhase()
    {
        LogAssert.ignoreFailingMessages = true;

        GameObject first = new("Stalked target");
        GameObject second = new("Passer by");
        spawned.Add(first);
        spawned.Add(second);

        EnemyTarget held = first.AddComponent<EnemyTarget>();
        EnemyTarget other = second.AddComponent<EnemyTarget>();

        EnemyTargetMemory memory = new();
        memory.SetTarget(held);
        memory.RememberObservation(held, Vector3.zero, Vector3.forward, 10f);

        EnemyStealthManeuver maneuver = new();
        maneuver.Begin(memory.LastObservation, EnemyState.Stalk, 10f);

        maneuver.EnterPhase(EnemyState.Retreat);
        maneuver.EnterPhase(EnemyState.Flank);

        Assert.That(maneuver.PhaseChanges, Is.EqualTo(2));
        Assert.That(maneuver.Observation.Target, Is.EqualTo(held));

        // Somebody else walking past does not move the spot this manoeuvre is
        // creeping towards.
        EnemyTargetMemory otherMemory = new();
        otherMemory.RememberObservation(
            other,
            new Vector3(30f, 0f, 0f),
            Vector3.back,
            11f);

        Assert.That(maneuver.TryRefresh(otherMemory.LastObservation), Is.False);
        Assert.That(maneuver.Observation.Position, Is.EqualTo(Vector3.zero));

        // The same person, seen again, does.
        memory.RememberObservation(
            held,
            new Vector3(4f, 0f, 0f),
            Vector3.right,
            12f);

        Assert.That(maneuver.TryRefresh(memory.LastObservation), Is.True);
        Assert.That(maneuver.Observation.Position.x, Is.EqualTo(4f));
    }

    // Each phase used to carry its own timer and reset the others by entering,
    // so Retreat and Flank could hand back and forth forever on a pose nobody
    // had checked the age of.
    [Test]
    public void StealthManeuver_EndsOnStaleEvidenceAndOnItsOwnDeadline()
    {
        LogAssert.ignoreFailingMessages = true;

        GameObject player = new("Observed target");
        spawned.Add(player);

        EnemyTarget target = player.AddComponent<EnemyTarget>();
        EnemyTargetMemory memory = new();
        memory.RememberObservation(target, Vector3.zero, Vector3.forward, 100f);

        EnemyStealthManeuver maneuver = new();
        maneuver.Begin(memory.LastObservation, EnemyState.Stalk, 100f);

        Assert.That(
            maneuver.Evaluate(105f, 10f, 25f, out _),
            Is.EqualTo(EnemyStealthManeuverStatus.Running));

        Assert.That(
            maneuver.Evaluate(115f, 10f, 25f, out _),
            Is.EqualTo(EnemyStealthManeuverStatus.ObservationLost));

        // Kept fresh, the manoeuvre still ends - sneaking has had its chance.
        memory.RememberObservation(target, Vector3.zero, Vector3.forward, 120f);
        maneuver.TryRefresh(memory.LastObservation);

        Assert.That(
            maneuver.Evaluate(126f, 10f, 25f, out _),
            Is.EqualTo(EnemyStealthManeuverStatus.Expired));

        maneuver.End();

        Assert.That(
            maneuver.Evaluate(126f, 10f, 25f, out _),
            Is.EqualTo(EnemyStealthManeuverStatus.ObservationLost));
    }

    // A NavMesh route out of a dead end runs back down the corridor the
    // watcher is standing in. The straight line from where the enemy is to
    // where it is going never touches them, so the segment test called that a
    // retreat and the enemy walked past the player's feet to reach it.
    [Test]
    public void RouteRules_CatchARouteThatDoublesBackPastTheWatcher()
    {
        Vector3 watcher = Vector3.zero;
        Vector3 from = new(0f, 0f, 10f);
        Vector3 to = new(0f, 0f, 12f);

        Assert.That(
            EnemyStateRules.ClosesOnWatcher(from, to, watcher),
            Is.False);

        Vector3[] route =
        {
            from,
            new(0f, 0f, 1f),
            to,
        };

        Assert.That(
            EnemyStateRules.RouteClosesOnWatcher(route, watcher),
            Is.True);

        // A route that leads away the whole time is still a retreat.
        Vector3[] straightAway =
        {
            from,
            new(0f, 0f, 14f),
            new(0f, 0f, 20f),
        };

        Assert.That(
            EnemyStateRules.RouteClosesOnWatcher(straightAway, watcher),
            Is.False);
    }

    [Test]
    public void TacticalSlotRegistry_KeepsTwoFlankingEnemiesOffOneSpot()
    {
        EnemyTacticalSlotRegistry.ResetForTests();

        Vector3 behindThePlayer = new(3f, 0f, 0f);
        EnemyTacticalSlotRegistry.Claim(1, behindThePlayer);

        Assert.That(
            EnemyTacticalSlotRegistry.IsClaimedByAnother(
                2,
                behindThePlayer + new Vector3(0.5f, 0f, 0f),
                spacing: 2f),
            Is.True);

        // Its own claim never blocks it, or it would refuse to keep the spot
        // it is already walking to.
        Assert.That(
            EnemyTacticalSlotRegistry.IsClaimedByAnother(
                1,
                behindThePlayer,
                spacing: 2f),
            Is.False);

        Assert.That(
            EnemyTacticalSlotRegistry.IsClaimedByAnother(
                2,
                behindThePlayer + new Vector3(4f, 0f, 0f),
                spacing: 2f),
            Is.False);

        EnemyTacticalSlotRegistry.Release(1);

        Assert.That(
            EnemyTacticalSlotRegistry.IsClaimedByAnother(
                2,
                behindThePlayer,
                spacing: 2f),
            Is.False);

        EnemyTacticalSlotRegistry.ResetForTests();
    }

    // Look and take were two calls several ticks apart, and in between the
    // enemy next to this one found the same gap behind the same player.
    [Test]
    public void TacticalSlotRegistry_TryClaimRefusesASpotSomebodyElseTook()
    {
        EnemyTacticalSlotRegistry.ResetForTests();

        Vector3 behindThePlayer = new(3f, 0f, 0f);

        Assert.That(
            EnemyTacticalSlotRegistry.TryClaim(1, behindThePlayer, spacing: 2f),
            Is.True);

        Assert.That(
            EnemyTacticalSlotRegistry.TryClaim(
                2,
                behindThePlayer + new Vector3(0.5f, 0f, 0f),
                spacing: 2f),
            Is.False);

        // Refused means not taken: the loser must not have quietly overwritten
        // the winner on its way out.
        Assert.That(
            EnemyTacticalSlotRegistry.IsClaimedByAnother(
                1,
                behindThePlayer,
                spacing: 2f),
            Is.False);

        EnemyTacticalSlotRegistry.ResetForTests();
    }

    // A tick with no budget checks nothing, so it must leave the fan where it
    // found it. Stepping the cursor by the whole per-tick allowance regardless
    // marked candidates as searched that nobody had looked at, and the search
    // then reported the level had nothing to offer.
    [Test]
    public void RetreatPlanner_KeepsItsPlaceWhenTheBudgetRunsOut()
    {
        EnemyStealthTacticsConfig tactics =
            ScriptableObject.CreateInstance<EnemyStealthTacticsConfig>();
        EnemyConfig config = ScriptableObject.CreateInstance<EnemyConfig>();
        TestReflection.SetField(config, "stealthTacticsProfile", tactics);

        EnemyBrainContext context = new(
            config,
            null,
            null,
            null,
            null,
            null,
            new EnemyBlackboard(),
            null,
            null);

        EnemyRetreatPlanner planner = new(context);
        List<Vector3> threats = new() { new Vector3(0f, 0f, 4f) };

        int fanSize =
            tactics.retreatAngles.Length * tactics.retreatDistanceScales.Length;
        int ticksToWalkTheWholeFan =
            Mathf.CeilToInt(fanSize / (float)tactics.candidatesPerTick);

        // Denied every tick, so nothing is ever judged and the answer is
        // always "ask again", never "there is nowhere to go".
        for (int tick = 0; tick < ticksToWalkTheWholeFan + 1; tick++)
        {
            int noPathQueries = 0;
            int noVisibilityQueries = 0;

            Assert.That(
                planner.TryFindRetreatPoint(
                    Vector3.zero,
                    threats,
                    ref noPathQueries,
                    ref noVisibilityQueries,
                    out _),
                Is.EqualTo(EnemyTacticalPlanResult.Deferred),
                $"tick {tick} claimed candidates it never checked");
        }

        // With budget, every candidate reaches an answer - there is no NavMesh
        // in an edit-mode test, so the answer is "nowhere" - and the fan does
        // finish rather than deferring for ever.
        EnemyTacticalPlanResult result = EnemyTacticalPlanResult.Deferred;

        for (int tick = 0; tick < ticksToWalkTheWholeFan; tick++)
        {
            int pathQueries = fanSize;
            int visibilityQueries = fanSize;

            result = planner.TryFindRetreatPoint(
                Vector3.zero,
                threats,
                ref pathQueries,
                ref visibilityQueries,
                out _);
        }

        Assert.That(result, Is.EqualTo(EnemyTacticalPlanResult.NotFound));

        Object.DestroyImmediate(config);
        Object.DestroyImmediate(tactics);
    }

    // One queue for both resources meant an enemy waiting on raycasts held up
    // an enemy that only wanted a path, and a state's tactical plan queued
    // behind its own navigator's repath.
    [Test]
    public void PerceptionScheduler_QueuesPathAndVisibilityWorkSeparately()
    {
        EnemyServerPerceptionScheduler.ResetForTests();

        int pathBudget = EnemyServerPerceptionScheduler.PathQueriesRemainingThisFrame;

        Assert.That(
            EnemyServerPerceptionScheduler.TryReservePathQueries(
                enemyId: 1,
                requestedQueries: pathBudget,
                out _),
            Is.True);

        // Out of path budget, so this one waits at the head of the path queue.
        Assert.That(
            EnemyServerPerceptionScheduler.TryReservePathQueries(
                enemyId: 1,
                requestedQueries: 1,
                out _),
            Is.False);

        // Which says nothing about raycasts, and used to.
        Assert.That(
            EnemyServerPerceptionScheduler.TryReserveVisibilityQueries(
                enemyId: 2,
                requestedQueries: 4,
                out int grantedVisibility),
            Is.True);
        Assert.That(grantedVisibility, Is.EqualTo(4));

        EnemyServerPerceptionScheduler.ResetForTests();
    }

    // An enemy asking twice in one frame - its planner and then its navigator -
    // only yields to somebody actually waiting.
    [Test]
    public void PerceptionScheduler_ServesASecondRequestWhenNobodyIsWaiting()
    {
        EnemyServerPerceptionScheduler.ResetForTests();

        Assert.That(
            EnemyServerPerceptionScheduler.TryReservePathQueries(
                enemyId: 7,
                requestedQueries: 4,
                out _),
            Is.True);

        Assert.That(
            EnemyServerPerceptionScheduler.TryReservePathQueries(
                enemyId: 7,
                requestedQueries: 4,
                out int grantedAgain),
            Is.True);
        Assert.That(grantedAgain, Is.EqualTo(4));

        EnemyServerPerceptionScheduler.ResetForTests();
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
