using System;
using NUnit.Framework;
using UnityEngine;

internal sealed class EnemyAttackEffectProbe : IEnemyAttackEffect
{
    internal bool Result { get; set; } = true;
    internal int ApplyCount { get; private set; }

    public bool TryApply(EnemyAttackContext context)
    {
        ApplyCount++;
        return Result;
    }
}

[Category("Gameplay")]
public sealed class EnemyCombatAndStateLogicTests
{
    [TestCase(EnemyAttackResultType.Started, true, false, false)]
    [TestCase(EnemyAttackResultType.Hit, false, true, false)]
    [TestCase(EnemyAttackResultType.Interrupted, false, false, true)]
    [TestCase(EnemyAttackResultType.OutOfRange, false, false, true)]
    [TestCase(EnemyAttackResultType.LineOfHitBlocked, false, false, true)]
    [TestCase(EnemyAttackResultType.InvalidTarget, false, false, true)]
    [TestCase(EnemyAttackResultType.Busy, false, false, false)]
    public void AttackResult_ExposesStableOutcomeClassification(
        EnemyAttackResultType type,
        bool started,
        bool applied,
        bool interrupted)
    {
        EnemyAttackResult result = EnemyAttackResult.Create(
            type,
            EnemyTargetIdentity.None,
            Vector3.one,
            Vector3.zero);

        Assert.That(result.WasStarted, Is.EqualTo(started));
        Assert.That(result.WasApplied, Is.EqualTo(applied));
        Assert.That(result.WasInterrupted, Is.EqualTo(interrupted));
    }

    [Test]
    public void AttackPhaseSnapshot_ClampsTimeAndTracksElapsedServerTime()
    {
        EnemyAttackPhaseSnapshot snapshot = new(
            EnemyAttackPhase.AttackWindup,
            EnemyTargetIdentity.None,
            Vector3.left,
            Vector3.right,
            EnemyAttackResultType.None,
            -10d);

        Assert.That(snapshot.StartedServerTime, Is.Zero);
        Assert.That(snapshot.IsActive, Is.True);
        Assert.That(snapshot.GetElapsedTime(-1d), Is.Zero);
        Assert.That(snapshot.GetElapsedTime(2.5d), Is.EqualTo(2.5f));

        EnemyAttackPhaseSnapshot idle = EnemyAttackPhaseSnapshot.CreateIdle(5d);
        Assert.That(idle.IsActive, Is.False);
        Assert.That(idle.HasTarget, Is.False);
        Assert.That(idle.StartedServerTime, Is.EqualTo(5d));
    }

    [Test]
    public void AttackPipeline_RejectsMissingTargetWithoutChangingPhase()
    {
        GameObject contextObject = new("Attack context");

        try
        {
            EnemyAttackPipeline pipeline = new(
                new EnemyAttackEffectProbe(),
                new EnemyAttackCooldown(),
                new EnemyAttackContextFactory(),
                new EnemyLineOfHitValidator(),
                consumeCooldownOnFailedEffect: false,
                contextObject.transform);

            EnemyAttackResult result = pipeline.TryStartAttack(
                null,
                null,
                Vector3.zero,
                null);

            Assert.That(result.Type, Is.EqualTo(EnemyAttackResultType.InvalidTarget));
            Assert.That(pipeline.Phase, Is.EqualTo(EnemyAttackPhase.Idle));
            Assert.That(pipeline.IsBusy, Is.False);
            Assert.DoesNotThrow(() => pipeline.Tick(-10f, Vector3.zero));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(contextObject);
        }
    }

    [Test]
    public void AttackPipeline_RequiresEveryDependency()
    {
        GameObject contextObject = new("Attack dependencies");
        EnemyAttackEffectProbe effect = new();
        EnemyAttackCooldown cooldown = new();
        EnemyAttackContextFactory factory = new();
        EnemyLineOfHitValidator validator = new();

        try
        {
            Assert.Throws<ArgumentNullException>(() => new EnemyAttackPipeline(
                null, cooldown, factory, validator, false, contextObject.transform));
            Assert.Throws<ArgumentNullException>(() => new EnemyAttackPipeline(
                effect, null, factory, validator, false, contextObject.transform));
            Assert.Throws<ArgumentNullException>(() => new EnemyAttackPipeline(
                effect, cooldown, null, validator, false, contextObject.transform));
            Assert.Throws<ArgumentNullException>(() => new EnemyAttackPipeline(
                effect, cooldown, factory, null, false, contextObject.transform));
            Assert.Throws<ArgumentNullException>(() => new EnemyAttackPipeline(
                effect, cooldown, factory, validator, false, null));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(contextObject);
        }
    }

    [Test]
    public void Blackboard_ClearAllResetsEveryGameplayMemory()
    {
        EnemyBlackboard blackboard = new();
        blackboard.SetCurrentPosture(EnemyPosture.Crawling);
        blackboard.SetCurrentDestination(new Vector3(2f, 0f, 3f));
        blackboard.InvestigationMemory.RememberSuspiciousPosition(Vector3.one);
        blackboard.InvestigationMemory.RememberLastKnownTargetPosition(Vector3.right);
        blackboard.SetCurrentStimulus(
            EnemyPerceptionStimulus.ForSuspiciousPosition(
                Vector3.forward,
                0.5f,
                EnemyPerceptionSource.Hearing),
            4f);

        Assert.That(blackboard.HasCurrentDestination, Is.True);
        Assert.That(blackboard.HasSuspiciousPosition, Is.True);
        Assert.That(blackboard.HasLastKnownTargetPosition, Is.True);
        Assert.That(blackboard.CurrentStimulus.HasStimulus, Is.True);

        blackboard.ClearAll();

        Assert.That(blackboard.HasCurrentDestination, Is.False);
        Assert.That(blackboard.CurrentDestination, Is.EqualTo(Vector3.zero));
        Assert.That(blackboard.CurrentPosture, Is.EqualTo(EnemyPosture.Standing));
        Assert.That(blackboard.HasSuspiciousPosition, Is.False);
        Assert.That(blackboard.HasLastKnownTargetPosition, Is.False);
        Assert.That(blackboard.CurrentStimulus.HasStimulus, Is.False);
        Assert.That(blackboard.CurrentTarget, Is.Null);
    }

    [Test]
    public void PerceptionDecisionFactories_AreExplicit()
    {
        Assert.That(EnemyPerceptionDecision.None.HasDecision, Is.False);
        Assert.That(
            EnemyPerceptionDecision.ConfirmedTarget().Type,
            Is.EqualTo(EnemyPerceptionDecisionType.ConfirmedTarget));
        Assert.That(
            EnemyPerceptionDecision.SuspiciousPosition().Type,
            Is.EqualTo(EnemyPerceptionDecisionType.SuspiciousPosition));
    }

    [Test]
    public void DoorNavigationResult_DistinguishesStopMoveAndNoop()
    {
        EnemyDoorNavigationResult none = EnemyDoorNavigationResult.None;
        EnemyDoorNavigationResult stop = EnemyDoorNavigationResult.Stop();
        EnemyDoorNavigationResult move =
            EnemyDoorNavigationResult.MoveTo(new Vector3(5f, 0f, 1f));

        Assert.That(none.IsHandled, Is.False);
        Assert.That(stop.IsHandled, Is.True);
        Assert.That(stop.ShouldStop, Is.True);
        Assert.That(stop.HasOverrideDestination, Is.False);
        Assert.That(move.IsHandled, Is.True);
        Assert.That(move.ShouldStop, Is.False);
        Assert.That(move.HasOverrideDestination, Is.True);
        Assert.That(move.OverrideDestination, Is.EqualTo(new Vector3(5f, 0f, 1f)));
    }

    [Test]
    public void AttackProfiles_ClampUnsafeRuntimeValues()
    {
        EnemyAttackTimingConfig timing =
            ScriptableObject.CreateInstance<EnemyAttackTimingConfig>();
        EnemyAttackHitValidationConfig hit =
            ScriptableObject.CreateInstance<EnemyAttackHitValidationConfig>();

        try
        {
            timing.attackCooldown = -1f;
            timing.attackWindupDuration = -2f;
            timing.attackCommitDuration = -3f;
            timing.attackRecoveryDuration = -4f;
            timing.attackInterruptedDuration = -5f;
            timing.Validate();

            Assert.That(timing.attackCooldown, Is.Zero);
            Assert.That(timing.attackWindupDuration, Is.Zero);
            Assert.That(timing.attackCommitDuration, Is.Zero);
            Assert.That(timing.attackRecoveryDuration, Is.Zero);
            Assert.That(timing.attackInterruptedDuration, Is.Zero);

            hit.attackDistance = -2f;
            hit.attackCommitDistanceTolerance = -3f;
            hit.attackLineOfHitOriginHeight = -4f;
            hit.validateLineOfHit = false;
            hit.Validate(stoppingDistance: 1.25f);

            Assert.That(hit.attackDistance, Is.EqualTo(1.25f));
            Assert.That(hit.attackCommitDistanceTolerance, Is.Zero);
            Assert.That(hit.attackLineOfHitOriginHeight, Is.Zero);
            Assert.That(hit.CommitMaxDistance, Is.EqualTo(1.25f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(timing);
            UnityEngine.Object.DestroyImmediate(hit);
        }
    }

    [Test]
    public void TargetMemory_NullRefreshCannotLeaveStaleIdentity()
    {
        EnemyTargetMemory memory = new();
        memory.RefreshConfirmedTarget(null);

        Assert.That(memory.HasTarget, Is.False);
        Assert.That(memory.IsCurrentTargetValid, Is.False);
        Assert.That(memory.CurrentTargetIdentity, Is.EqualTo(EnemyTargetIdentity.None));
        Assert.That(
            memory.CurrentTargetClientId,
            Is.EqualTo(EnemyTargetIdentity.NoTargetClientId));
    }
}
