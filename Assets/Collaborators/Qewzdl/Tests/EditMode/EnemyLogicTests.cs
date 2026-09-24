using System;
using System.Collections.Generic;
using System.Linq;
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

            // Installed, because following the live target is a behaviour now
            // rather than the default. The assertions below are about what the
            // behaviour does, so they have to be made of an enemy that has it.
            memory.InstallLiveTargetTracking();

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

            // Nothing installed, which is now what "does not follow you
            // through walls" is made of. It used to be an argument to this
            // call; it is a behaviour the enemy either has or has not.
            Assert.That(
                memory.TryStartVisualMemoryGracePeriod(target, 2f),
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

    // The three states RegisterStateHandlers installs unconditionally. Written
    // here rather than read off the brain because that is the point: if
    // somebody makes one of these optional, the chains below stop landing
    // anywhere and this file should be what says so.
    // The one state the brain installs itself, and the one every fallback
    // chain has to end at. Written here rather than read off the brain because
    // that is the point: if somebody makes standing still optional, the chains
    // stop landing anywhere and this file should be what says so.
    private static readonly EnemyState[] FloorStates =
    {
        EnemyState.Idle,
    };

    // Everything a module can install: the nine states less standing still,
    // which no module provides because it is not a behaviour.
    private static EnemyState[] ModuleInstalledStates =>
        AllStates.Where(state => Array.IndexOf(FloorStates, state) < 0).ToArray();

    private static readonly EnemyState[] AllStates =
    {
        EnemyState.Idle,
        EnemyState.Patrol,
        EnemyState.Chase,
        EnemyState.Attack,
        EnemyState.Investigate,
        EnemyState.Stalk,
        EnemyState.Retreat,
        EnemyState.Flank,
        EnemyState.Ambush,
    };

    // The invariant the whole behaviour set rests on: whatever is switched off,
    // a transition request has somewhere to land.
    //
    // An enemy built without a behaviour can still be asked for it - perception
    // does not know what is installed, and should not have to. ChangeState
    // answers by walking this chain, so a chain that runs out, or runs round,
    // is an enemy that freezes in whatever she was doing with the stimulus
    // unanswered. That failure is silent in play: she just stands there.
    [Test]
    public void StateFallbacks_FromAnyState_EndAtAStateThatIsAlwaysInstalled()
    {
        foreach (EnemyState start in AllStates)
        {
            EnemyState state = start;
            int steps = 0;

            while (Array.IndexOf(FloorStates, state) < 0)
            {
                Assert.That(
                    EnemyStateRules.TryGetFallback(state, out EnemyState next),
                    Is.True,
                    $"{state} is neither the floor nor has a fallback, so " +
                    "an enemy built without it has nowhere to go when asked " +
                    "for it.");

                state = next;
                steps++;

                Assert.That(
                    steps,
                    Is.LessThan(EnemyStateRules.FallbackChainLimit),
                    $"Fallback chain from {start} does not terminate.");
            }
        }
    }

    // The floor must NOT have a fallback. One that did would be a state the
    // brain always installs and yet quietly redirects away from, which is a
    // transition nobody asked for and nothing would explain.
    [Test]
    public void StateFallbacks_ForTheFloorState_AreRefused()
    {
        foreach (EnemyState state in FloorStates)
        {
            Assert.That(
                EnemyStateRules.TryGetFallback(state, out _),
                Is.False,
                $"{state} is installed on every enemy, so redirecting away " +
                "from it can only ever be wrong.");
        }
    }

    // The four phases of sneaking collapse to the same thing, which is what
    // makes them one switch rather than four. If they ever disagree, switching
    // the behaviour off would leave an enemy who chases from one phase and
    // idles from another, depending on where the manoeuvre happened to be.
    [Test]
    public void StateFallbacks_ForEveryStealthPhase_AgreeOnChase()
    {
        foreach (EnemyState state in AllStates)
        {
            if (!EnemyStateRules.IsStealthManeuver(state))
            {
                continue;
            }

            Assert.That(
                EnemyStateRules.TryGetFallback(state, out EnemyState fallback),
                Is.True);

            Assert.That(
                fallback,
                Is.EqualTo(EnemyState.Chase),
                $"{state} falls back somewhere other than the rest of the " +
                "manoeuvre does.");
        }
    }

    // Every module the project ships installs at least one state, and the
    // states they install between them are exactly the nine the brain used to
    // hardcode - no more, and none missing.
    //
    // This is the test that would have caught a module quietly claiming a state
    // another one also claims, and the one that fails if somebody adds a state
    // to the enum and forgets to give any module a way to install it: a state
    // nothing installs is a state ChangeState can only ever fall back away
    // from, which is a behaviour that exists in the enum and nowhere else.
    [Test]
    public void ShippedBehaviorModules_BetweenThem_InstallEveryState()
    {
        Dictionary<EnemyState, IEnemyStateHandler> handlers = new();
        EnemyBehaviorCapabilities capabilities = new();
        EnemyBehaviorInstaller installer = new(null, handlers, capabilities);

        int claimed = 0;

        foreach (EnemyBehaviorModule module in CreateShippedStateModules())
        {
            try
            {
                int before = handlers.Count;
                module.Install(installer);

                Assert.That(
                    handlers.Count,
                    Is.GreaterThan(before),
                    $"{module.GetType().Name} installed nothing at all.");

                claimed += handlers.Count - before;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(module);
            }
        }

        Assert.That(
            handlers.Keys,
            Is.EquivalentTo(ModuleInstalledStates),
            "The shipped modules do not install exactly the eight states that " +
            "are behaviours. Standing still is the ninth and is the brain's.");

        Assert.That(
            claimed,
            Is.EqualTo(ModuleInstalledStates.Length),
            "Two modules claimed the same state, so one of them silently " +
            "replaced the other depending on list order.");
    }

    // A capability is found by the type a state asks for, and absence is an
    // answer rather than an exception. States are written to carry on without
    // one, so a registry that threw would turn "this enemy cannot check boxes"
    // into "this enemy crashes on every investigation".
    [Test]
    public void Capabilities_WhenNotInstalled_AreSimplyAbsent()
    {
        EnemyBehaviorCapabilities capabilities = new();

        Assert.That(capabilities.Has<EnemyInvestigationSearchPlanner>(), Is.False);
        Assert.That(
            capabilities.TryGet(out EnemyInvestigationSearchPlanner _),
            Is.False);

        EnemyInvestigationSearchPlanner installed = new();
        capabilities.Add(installed);

        Assert.That(capabilities.Has<EnemyInvestigationSearchPlanner>(), Is.True);
        Assert.That(
            capabilities.TryGet(out EnemyInvestigationSearchPlanner found),
            Is.True);
        Assert.That(found, Is.SameAs(installed));
    }

    // An enemy nobody configured has to be the enemy this game shipped with.
    //
    // An empty behaviour list is legal and means the default set, so every
    // config in the project that has not been filled in yet depends entirely on
    // this staying complete. A behaviour missing from it is missing from all of
    // them, and the symptom is an enemy that still walks, still searches, and
    // quietly stopped doing one thing.
    //
    // The hiding place check was missing from this set for exactly one commit,
    // and the only tests that caught it were two that built the state by hand.
    // Those now install the capability themselves - correctly, since they are
    // testing the state rather than the wiring - which left nothing at all
    // watching this. Hence a test that asks the question directly.
    [Test]
    public void EveryBehavior_CoversEveryModuleInstalledState()
    {
        Dictionary<EnemyState, IEnemyStateHandler> handlers = new();
        EnemyBehaviorCapabilities capabilities = new();

        EnemyEveryBehavior.Install(
            new EnemyBehaviorInstaller(
                CreateContextWithoutANavigator(),
                handlers,
                capabilities));

        Assert.That(
            handlers.Keys,
            Is.EquivalentTo(ModuleInstalledStates),
            "The full set no longer covers every state a module provides.");

        Assert.That(
            capabilities.Has<EnemyHidingPlaceCheck>(),
            Is.True,
            "The full set no longer checks hiding places. That used to live " +
            "inside the investigating state and came free with every enemy " +
            "in the game.");

        // The same trap as the hiding check, twice over: both of these were
        // parts of the investigating state until they were pulled out, so
        // neither looks like something the enemy is supposed to be given.
        Assert.That(
            capabilities.Has<EnemyLookAround>(),
            Is.True,
            "The full set no longer pauses to look around on arrival.");

        Assert.That(
            capabilities.Has<EnemySearchRoute>(),
            Is.True,
            "The full set no longer searches around a stimulus, only at it.");
    }

    // Doors, barricades and both senses install themselves into a component
    // on the enemy rather than into the capability registry, so the test above
    // cannot see them: it hands the installer a context with no components, and
    // a module that reaches through it quietly does nothing.
    //
    // That silence is the hazard. Both modules are one missing null check away
    // from being list entries the inspector shows and the game ignores, and the
    // only other thing that would notice is a PlayMode test about pathing. So
    // this asserts they ask the navigator for something - the asking is what
    // cannot be checked anywhere cheaper.
    [Test]
    public void ComponentBehaviorModules_WithNoComponents_DoNotThrow()
    {
        Dictionary<EnemyState, IEnemyStateHandler> handlers = new();
        EnemyBehaviorCapabilities capabilities = new();
        EnemyBehaviorInstaller installer = new(
            CreateContextWithoutANavigator(),
            handlers,
            capabilities);

        foreach (EnemyBehaviorModule module in CreateComponentModules())
        {
            try
            {
                Assert.DoesNotThrow(
                    () => module.Install(installer),
                    $"{module.GetType().Name} cannot cope with an enemy " +
                    "missing the component it reaches for, so building one " +
                    "would take the whole enemy down rather than one " +
                    "behaviour.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(module);
            }
        }

        Assert.That(
            handlers,
            Is.Empty,
            "A component module claimed a state, which would take it off " +
            "whichever state module was supposed to provide it.");
    }

    // A context is always built, whatever the enemy is missing - only the
    // components hanging off it can be absent. Modelling it the other way round
    // and passing no context at all would have these tests demanding a null
    // check from every module for a case production cannot produce.
    private static EnemyBrainContext CreateContextWithoutANavigator()
    {
        return new EnemyBrainContext(
            null,
            null,
            null,
            null,
            null,
            null,
            new EnemyBlackboard(),
            null,
            null);
    }

    // Modules that reach for a component on the enemy rather than adding a
    // state or a capability. They all have the same hazard - a null component
    // and they quietly do nothing - and none of them may claim a state.
    private static IEnumerable<EnemyBehaviorModule> CreateComponentModules()
    {
        yield return ScriptableObject.CreateInstance<EnemyDoorTraversalModule>();
        yield return ScriptableObject.CreateInstance<EnemyItemPushingModule>();
        yield return ScriptableObject.CreateInstance<EnemySightModule>();
        yield return ScriptableObject.CreateInstance<EnemyHearingModule>();
        yield return ScriptableObject.CreateInstance<EnemyCrawlingModule>();
    }

    // The state modules only. Capability modules install no states at all,
    // which is the whole of what makes them a different kind of module, so
    // holding them to "installed something" would be holding them to the one
    // rule they exist to break.
    private static IEnumerable<EnemyBehaviorModule> CreateShippedStateModules()
    {
        yield return ScriptableObject.CreateInstance<EnemyChaseBehaviorModule>();
        yield return ScriptableObject.CreateInstance<EnemyAttackBehaviorModule>();
        yield return ScriptableObject.CreateInstance<EnemyPatrolBehaviorModule>();
        yield return ScriptableObject
            .CreateInstance<EnemyInvestigationBehaviorModule>();
        yield return ScriptableObject.CreateInstance<EnemyStalkBehaviorModule>();
        yield return ScriptableObject.CreateInstance<EnemyRetreatBehaviorModule>();
        yield return ScriptableObject.CreateInstance<EnemyFlankBehaviorModule>();
        yield return ScriptableObject.CreateInstance<EnemyAmbushBehaviorModule>();
    }

    // A capability module adds something the states can look up and claims no
    // state of its own.
    //
    // Both halves matter. One that installed a state would be a state module
    // wearing the wrong name, and one that installed nothing at all would be a
    // module the enemy config lists, the inspector shows, and the game ignores
    // - which is the failure a reader has no way to see.
    [Test]
    public void HidingPlaceCheckModule_InstallsACapabilityAndNoState()
    {
        Dictionary<EnemyState, IEnemyStateHandler> handlers = new();
        EnemyBehaviorCapabilities capabilities = new();
        EnemyBehaviorInstaller installer = new(null, handlers, capabilities);

        EnemyHidingPlaceCheckModule module =
            ScriptableObject.CreateInstance<EnemyHidingPlaceCheckModule>();

        try
        {
            module.Install(installer);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(module);
        }

        Assert.That(
            handlers,
            Is.Empty,
            "A capability module claimed a state, which would take it off " +
            "whichever state module was supposed to provide it.");

        Assert.That(
            capabilities.Has<EnemyHidingPlaceCheck>(),
            Is.True,
            "The hiding check module installed nothing, so an enemy listing " +
            "it gains nothing and no test but this one would notice.");
    }

    // What sends her to an investigation decides how fast she goes: a heard
    // noise is weighed, anything else is a chase that lost its target.
    [Test]
    public void ApproachSpeed_RunsAtLoudNoisesAndWalksAtQuietOnes()
    {
        const float threshold = 0.4f;
        const float chase = 2.8f;
        const float cautious = 1.7f;

        float Speed(EnemyPerceptionStimulus stimulus) =>
            EnemyInvestigateState.ChooseApproachSpeed(
                stimulus, threshold, chase, cautious);

        EnemyPerceptionStimulus Heard(float score) =>
            EnemyPerceptionStimulus.ForSuspiciousPosition(
                Vector3.zero, score, EnemyPerceptionSource.Hearing);

        Assert.That(Speed(Heard(0.2f)), Is.EqualTo(cautious),
            "A faint noise sent her running.");
        Assert.That(Speed(Heard(0.8f)), Is.EqualTo(chase),
            "A loud noise close by did not.");

        // At the threshold rather than past it: the tooltip promises the
        // number is where running starts.
        Assert.That(Speed(Heard(threshold)), Is.EqualTo(chase));

        // No stimulus is what an investigation looks like when it started
        // because she lost sight of somebody. Walking there would hand them
        // the escape.
        Assert.That(Speed(EnemyPerceptionStimulus.None), Is.EqualTo(chase),
            "Losing sight of somebody stopped being answered at a run.");

        Assert.That(
            Speed(EnemyPerceptionStimulus.ForSuspiciousPosition(
                Vector3.zero, 0.05f, EnemyPerceptionSource.Vision)),
            Is.EqualTo(chase),
            "Something seen was weighed like a noise.");
    }
}
