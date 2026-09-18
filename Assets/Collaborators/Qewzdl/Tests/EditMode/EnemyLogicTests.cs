using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

internal sealed class EnemyTargetHidingStateProbe :
    MonoBehaviour,
    IReplicatedPlayerHidingStateService
{
    internal HidingTransitionState State { get; set; } =
        HidingTransitionState.Available;
    internal bool Hidden
    {
        get => State == HidingTransitionState.Occupied;
        set => State = value
            ? HidingTransitionState.Occupied
            : HidingTransitionState.Available;
    }

    public bool IsHidden => Hidden;
    public bool IsInHidingSequence =>
        State != HidingTransitionState.Available;
    public HidingTransitionState HidingState => State;
    public HidingPoseType HidingPose => HidingPoseType.Standing;
    public bool CanPeek => false;
    public ulong HidingPlaceNetworkObjectId =>
        HidingPlaceInteractable.NoOccupantNetworkObjectId;
}

[Category("Baseline")]
public sealed class EnemyLogicTests
{
    [Test]
    public void EnemyTarget_HiddenPlayerCannotBeDetected()
    {
        GameObject player = new("Hidden enemy target");
        player.SetActive(false);

        try
        {
            player.AddComponent<NetworkObject>();
            EnemyTargetHidingStateProbe hidingState =
                player.AddComponent<EnemyTargetHidingStateProbe>();

            GameObject visibilityPoint = new("Visibility Point");
            visibilityPoint.transform.SetParent(player.transform, false);

            LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "EnemyTarget has invalid visibility configuration:"
                )
            );
            EnemyTarget target = player.AddComponent<EnemyTarget>();
            typeof(EnemyTarget)
                .GetField(
                    "visibilityPoints",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic
                )
                ?.SetValue(
                    target,
                    new[] { visibilityPoint.transform }
                );

            player.SetActive(true);

            Assert.That(target.CanBeDetected, Is.True);

            hidingState.State = HidingTransitionState.Entering;

            Assert.That(
                target.CanBeDetected,
                Is.True,
                "Entering players must remain visible to enemies."
            );

            hidingState.Hidden = true;

            Assert.That(target.CanBeDetected, Is.False);

            hidingState.State = HidingTransitionState.Exiting;

            Assert.That(
                target.CanBeDetected,
                Is.True,
                "Exiting players must be visible through the open hiding place."
            );

            hidingState.Hidden = false;

            Assert.That(target.CanBeDetected, Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

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
    public void PatrolRoute_FindsThePointNearestWhereTheSearchEnded()
    {
        GameObject routeObject = new("Patrol route");

        try
        {
            EnemyPatrolRoute route = routeObject.AddComponent<EnemyPatrolRoute>();

            Assert.That(
                route.NearestPointIndex(Vector3.zero),
                Is.EqualTo(-1),
                "A route with no points has nowhere to resume.");

            Transform[] points =
            {
                MakePoint(routeObject, new Vector3(0f, 0f, 0f)),
                null,
                MakePoint(routeObject, new Vector3(10f, 0f, 0f)),
                MakePoint(routeObject, new Vector3(20f, 0f, 0f))
            };

            TestReflection.SetField(route, "points", points);

            Assert.That(route.NearestPointIndex(new Vector3(19f, 0f, 0f)), Is.EqualTo(3));
            Assert.That(route.NearestPointIndex(new Vector3(9f, 0f, 3f)), Is.EqualTo(2));

            // The hole a designer left in the array is not a place to patrol.
            Assert.That(route.NearestPointIndex(new Vector3(5f, 0f, 0f)), Is.Not.EqualTo(1));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(routeObject);
        }
    }

    private static Transform MakePoint(GameObject parent, Vector3 position)
    {
        GameObject point = new("Patrol point");
        point.transform.SetParent(parent.transform);
        point.transform.position = position;

        return point.transform;
    }

    // She walks towards footsteps in silence and says something about a cough.
    //
    // This was asked for in those words - do not let her cry out at the sound
    // of somebody walking - and until now nothing checked it. The rule is
    // about kinds rather than loudness: a rhythm goes on for as long as
    // somebody is moving or winded, so a reaction to one repeats every
    // cooldown for the length of a chase and stops reading as a reaction at
    // all. An event is a door, a dropped tin, a cough - one thing, once.
    [Test]
    public void PresentationProfile_SaysNothingAboutRhythmsAndSomethingAboutEvents()
    {
        EnemyPresentationProfile profile =
            ScriptableObject.CreateInstance<EnemyPresentationProfile>();

        try
        {
            Assert.That(
                profile.ShouldReactAloudTo(GameplayNoiseSourceType.Footstep),
                Is.False,
                "She would exclaim at every step anybody takes.");

            Assert.That(
                profile.ShouldReactAloudTo(GameplayNoiseSourceType.Breath),
                Is.False,
                "She would exclaim for as long as somebody is out of breath.");

            Assert.That(
                profile.ShouldReactAloudTo(GameplayNoiseSourceType.Player),
                Is.True,
                "A cough is the loudest thing a player does by accident and " +
                "she would let it pass without a word.");

            Assert.That(
                profile.ShouldReactAloudTo(GameplayNoiseSourceType.Door),
                Is.True,
                "A door is an event.");

            // The switch exists for somebody who wants her chattier, and it
            // has to actually reach the rhythms - that is the only thing it
            // was ever for.
            TestReflection.SetField(profile, "reactsToFootsteps", true);

            Assert.That(
                profile.ShouldReactAloudTo(GameplayNoiseSourceType.Footstep),
                Is.True,
                "Turning the switch on left footsteps silent.");

            Assert.That(
                profile.ShouldReactAloudTo(GameplayNoiseSourceType.Breath),
                Is.True,
                "Turning the switch on left breathing silent.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    [Test]
    public void SearchPlan_LeansTowardsWhereTheTargetWasHeaded()
    {
        Vector3 origin = new(4f, 0f, 4f);

        // Seen at the origin, walking down positive Z.
        Assert.That(
            EnemyInvestigationSearchPlanner.TryGetLeadOrigin(
                origin,
                origin,
                Vector3.forward,
                leadDistance: 1.5f,
                branchRadius: 2.5f,
                out Vector3 lead),
            Is.True);

        Assert.That((lead - origin).magnitude, Is.EqualTo(1.5f).Within(0.001f));
        Assert.That(
            Vector3.Dot((lead - origin).normalized, Vector3.forward),
            Is.EqualTo(1f).Within(0.001f));

        // A sighting from somewhere else entirely is a different event - this
        // is what an investigated noise looks like, and it must not inherit
        // the direction of an older chase.
        Assert.That(
            EnemyInvestigationSearchPlanner.TryGetLeadOrigin(
                origin,
                origin + Vector3.right * 9f,
                Vector3.forward,
                leadDistance: 1.5f,
                branchRadius: 2.5f,
                out Vector3 unled),
            Is.False);

        Assert.That(unled, Is.EqualTo(origin));

        // Height alone is not a direction to search in.
        Assert.That(
            EnemyInvestigationSearchPlanner.TryGetLeadOrigin(
                origin,
                origin,
                Vector3.up,
                leadDistance: 1.5f,
                branchRadius: 2.5f,
                out Vector3 upright),
            Is.False);

        Assert.That(upright, Is.EqualTo(origin));

        // Zero turns the whole thing off and leaves the ring where it was.
        Assert.That(
            EnemyInvestigationSearchPlanner.TryGetLeadOrigin(
                origin,
                origin,
                Vector3.forward,
                leadDistance: 0f,
                branchRadius: 2.5f,
                out Vector3 disabled),
            Is.False);

        Assert.That(disabled, Is.EqualTo(origin));
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

    [Test]
    public void PerceptionMemory_VisualGraceDeliberatelyFollowsLiveTargetPosition()
    {
        GameObject player = new("Visual memory target");
        player.SetActive(false);

        try
        {
            EnemyTarget target = CreateUnconfiguredTarget(player);
            player.transform.position = new Vector3(0f, 0f, 5f);

            EnemyPerceptionMemory memory = new();

            Assert.That(memory.TryStartVisualMemoryGracePeriod(target, 2f), Is.True);
            Assert.That(memory.IsUsingVisualMemory, Is.True);
            Assert.That(memory.VisualMemoryTimeRemaining, Is.EqualTo(2f));
            Assert.That(
                memory.GetVisualMemoryTargetPosition(),
                Is.EqualTo(new Vector3(0f, 0f, 5f)));

            player.transform.position = new Vector3(9f, 0f, 5f);

            // Pins the deliberate design documented on EnemyPerceptionMemory:
            // during the grace period the enemy keeps tracking the live target
            // through walls instead of freezing on the last seen point.
            Assert.That(
                memory.GetVisualMemoryTargetPosition(),
                Is.EqualTo(new Vector3(9f, 0f, 5f)),
                "Visual memory intentionally follows the live target; freezing it would change enemy balance.");

            Assert.That(memory.TryStartVisualMemoryGracePeriod(null, 2f), Is.False);
            Assert.That(memory.TryStartVisualMemoryGracePeriod(target, 0f), Is.False);

            memory.CancelVisualMemory();

            Assert.That(memory.IsUsingVisualMemory, Is.False);
            Assert.That(memory.VisualMemoryTimeRemaining, Is.Zero);
            Assert.That(memory.GetVisualMemoryTargetPosition(), Is.EqualTo(Vector3.zero));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void PerceptionMemory_FrozenModeHoldsThePointWhereSightBroke()
    {
        GameObject player = new("Frozen memory target");
        player.SetActive(false);

        try
        {
            EnemyTarget target = CreateUnconfiguredTarget(player);
            player.transform.position = new Vector3(0f, 0f, 5f);

            EnemyPerceptionMemory memory = new();

            Assert.That(
                memory.TryStartVisualMemoryGracePeriod(
                    target,
                    2f,
                    trackLiveTargetPosition: false),
                Is.True);

            player.transform.position = new Vector3(9f, 0f, 5f);

            // The whole point of the switch: the player who steps aside behind
            // cover is no longer followed through it.
            Assert.That(
                memory.GetVisualMemoryTargetPosition(),
                Is.EqualTo(new Vector3(0f, 0f, 5f)));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void StimulusResolver_ConfirmedVisionOutranksLouderHearingButKeepsItAsSuspicion()
    {
        GameObject player = new("Seen enemy target");
        player.SetActive(false);

        try
        {
            EnemyTarget target = CreateUnconfiguredTarget(player);

            EnemyPerceptionStimulus vision =
                EnemyPerceptionStimulus.ForConfirmedTarget(
                    target,
                    new Vector3(1f, 0f, 1f),
                    1f,
                    EnemyPerceptionSource.Vision);

            EnemyPerceptionStimulus hearing =
                EnemyPerceptionStimulus.ForSuspiciousPosition(
                    new Vector3(-8f, 0f, 3f),
                    99f,
                    EnemyPerceptionSource.Hearing);

            EnemyStimulusResolveContext context = new(
                null,
                new EnemyBlackboard(),
                EnemyState.Patrol,
                vision,
                true,
                hearing,
                true,
                5f);

            EnemyStimulusResolution resolution = new EnemyStimulusResolver().Resolve(
                context,
                new EnemyStimulusResolverPolicy());

            Assert.That(
                resolution.Action,
                Is.EqualTo(EnemyStimulusResolutionAction.ChaseConfirmedTarget),
                "Eyes outrank ears regardless of score while visionAlwaysOverridesHearing is on.");

            Assert.That(resolution.PrimaryStimulus.Target, Is.EqualTo(target));

            Assert.That(
                resolution.HasSecondaryStimulus,
                Is.True,
                "A noise from somewhere else must survive as a secondary suspicion.");

            Assert.That(
                resolution.SecondaryStimulus.Position,
                Is.EqualTo(hearing.Position));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void StimulusResolver_HeardTargetOnlyStartsInvestigationNotChase()
    {
        GameObject player = new("Heard enemy target");
        player.SetActive(false);

        try
        {
            EnemyTarget target = CreateUnconfiguredTarget(player);

            EnemyPerceptionStimulus hearing =
                EnemyPerceptionStimulus.ForConfirmedTarget(
                    target,
                    new Vector3(3f, 0f, 0f),
                    4f,
                    EnemyPerceptionSource.Hearing);

            EnemyStimulusResolveContext context = new(
                null,
                new EnemyBlackboard(),
                EnemyState.Patrol,
                EnemyPerceptionStimulus.None,
                false,
                hearing,
                true,
                5f);

            EnemyStimulusResolution resolution = new EnemyStimulusResolver().Resolve(
                context,
                new EnemyStimulusResolverPolicy());

            Assert.That(
                resolution.Action,
                Is.EqualTo(EnemyStimulusResolutionAction.InvestigateSuspiciousPosition),
                "Ears alone must not confirm a target while confirmedHearingCanStartChase is off.");

            Assert.That(resolution.ShouldClearCurrentTarget, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    // EnemyTarget logs its visibility contract error the moment it is added,
    // before a test can configure it. Tests that only need target identity
    // swallow that one expected error here.
    private static EnemyTarget CreateUnconfiguredTarget(GameObject inactiveOwner)
    {
        LogAssert.Expect(
            LogType.Error,
            new System.Text.RegularExpressions.Regex(
                "EnemyTarget has invalid visibility configuration:"
            )
        );

        return inactiveOwner.AddComponent<EnemyTarget>();
    }

    [Test]
    public void PatrolProfile_ClampsUnsafeRoutePlanningValues()
    {
        EnemyPatrolConfig config =
            ScriptableObject.CreateInstance<EnemyPatrolConfig>();

        try
        {
            config.patrolRouteVariation = -1f;
            config.patrolEdgeClearance = -2f;
            config.patrolMaxDetourRatio = 0.5f;
            config.patrolIntermediatePointSpacing = 0f;
            config.patrolRouteSampleAttempts = 0;

            config.Validate();

            Assert.That(config.patrolRouteVariation, Is.Zero);
            Assert.That(config.patrolEdgeClearance, Is.Zero);
            Assert.That(config.patrolMaxDetourRatio, Is.EqualTo(1f));
            Assert.That(config.patrolIntermediatePointSpacing, Is.EqualTo(1f));
            Assert.That(config.patrolRouteSampleAttempts, Is.EqualTo(1));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(config);
        }
    }
}
