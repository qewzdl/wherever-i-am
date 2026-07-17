using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

[Category("Baseline")]
public sealed class EnemyLogicTests
{
    [Test]
    public void AttackCooldown_ClampsTicksAndCanBeReset()
    {
        EnemyAttackCooldown cooldown = new();

        cooldown.Start(-1f);
        Assert.That(cooldown.IsActive, Is.False);

        cooldown.Start(1f);
        Assert.That(cooldown.IsActive, Is.True);

        cooldown.Tick(0.4f);
        Assert.That(cooldown.IsActive, Is.True);

        cooldown.Tick(0.6f);
        Assert.That(cooldown.IsActive, Is.False);

        cooldown.Start(2f);
        cooldown.Reset();
        Assert.That(cooldown.IsActive, Is.False);
    }

    [Test]
    public void InvestigationMemory_PromotesSuspicionAndClearsItAtomically()
    {
        EnemyInvestigationMemory memory = new();
        Vector3 suspiciousPosition = new(1f, 2f, 3f);

        memory.RememberSuspiciousPosition(suspiciousPosition);

        Assert.That(memory.PromoteSuspiciousPositionToLastKnown(), Is.True);
        Assert.That(memory.HasSuspiciousPosition, Is.False);
        Assert.That(memory.TryGetLastKnownTargetPosition(out Vector3 remembered), Is.True);
        Assert.That(remembered, Is.EqualTo(suspiciousPosition));
        Assert.That(memory.PromoteSuspiciousPositionToLastKnown(), Is.False);
    }

    [Test]
    public void InvestigationMemory_CopiesRoutesAndRejectsInvalidActiveIndex()
    {
        EnemyInvestigationMemory memory = new();
        List<EnemyInvestigationSearchPoint> source = new()
        {
            new EnemyInvestigationSearchPoint(Vector3.one, 1, -1, 0, -1),
            new EnemyInvestigationSearchPoint(Vector3.right, 2, 0, 0, 0)
        };

        memory.SetCurrentInvestigationRoute(source);
        source.Clear();

        Assert.That(memory.CurrentInvestigationRoute.Count, Is.EqualTo(2));

        memory.SetActiveSearchRouteIndex(1);
        Assert.That(memory.ActiveSearchRouteIndex, Is.EqualTo(1));

        memory.SetActiveSearchRouteIndex(10);
        Assert.That(memory.HasActiveSearchRouteIndex, Is.False);

        memory.ClearAll();
        Assert.That(memory.CurrentInvestigationRoute, Is.Empty);
    }

    [Test]
    public void PerceptionMemory_TracksVisionHearingAndResetsAllTimestamps()
    {
        EnemyPerceptionMemory memory = new();
        EnemyPerceptionStimulus vision =
            EnemyPerceptionStimulus.ForSuspiciousPosition(
                Vector3.forward,
                1f,
                EnemyPerceptionSource.Vision);
        EnemyPerceptionStimulus hearing =
            EnemyPerceptionStimulus.ForSuspiciousPosition(
                Vector3.right,
                2f,
                EnemyPerceptionSource.Hearing);

        memory.SetCurrentStimulus(vision, 10f);
        Assert.That(memory.LastVisibleTime, Is.EqualTo(10f));
        Assert.That(memory.LastHeardTime, Is.EqualTo(-1f));

        memory.SetCurrentStimulus(hearing, 20f);
        Assert.That(memory.LastHeardTime, Is.EqualTo(20f));

        memory.ClearAll();
        Assert.That(memory.CurrentStimulus.HasStimulus, Is.False);
        Assert.That(memory.LastVisibleTime, Is.EqualTo(-1f));
        Assert.That(memory.LastHeardTime, Is.EqualTo(-1f));
        Assert.That(memory.IsUsingVisualMemory, Is.False);
    }

    [Test]
    public void StimulusResolver_InvestigatesSuspiciousHearingAndRejectsMissingPolicy()
    {
        EnemyStimulusResolver resolver = new();
        EnemyPerceptionStimulus hearing =
            EnemyPerceptionStimulus.ForSuspiciousPosition(
                new Vector3(4f, 0f, 2f),
                3f,
                EnemyPerceptionSource.Hearing);
        EnemyStimulusResolveContext context = new(
            null,
            null,
            EnemyState.Idle,
            EnemyPerceptionStimulus.None,
            false,
            hearing,
            true,
            5f);

        EnemyStimulusResolution resolution = resolver.Resolve(
            context,
            new EnemyStimulusResolverPolicy());

        Assert.That(resolution.HasResolution, Is.True);
        Assert.That(
            resolution.Action,
            Is.EqualTo(EnemyStimulusResolutionAction.InvestigateSuspiciousPosition));
        Assert.That(resolution.PrimaryStimulus.Position, Is.EqualTo(hearing.Position));
        Assert.That(resolution.ShouldClearCurrentTarget, Is.False);

        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(context, null));
    }

    [Test]
    public void TargetIdentity_NoneIsStableAndHasNoNetworkTarget()
    {
        EnemyTargetIdentity identity = EnemyTargetIdentity.FromTarget(null);

        Assert.That(identity, Is.EqualTo(EnemyTargetIdentity.None));
        Assert.That(identity.HasTarget, Is.False);
        Assert.That(
            identity.OwnerClientId,
            Is.EqualTo(EnemyTargetIdentity.NoTargetClientId));
        Assert.That(identity.TryGetNetworkObject(out _), Is.False);
    }
}
