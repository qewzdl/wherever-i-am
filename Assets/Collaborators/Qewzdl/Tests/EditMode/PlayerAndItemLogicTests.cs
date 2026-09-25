using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

internal sealed class PlayerComponentInitializationProbe :
    PlayerComponent,
    IPlayerSignalListener
{
    internal int InitializeCount { get; private set; }
    internal int CleanupCount { get; private set; }
    internal bool ReceivedMultiplayerFlag { get; private set; }
    internal bool ReceivedOwnerFlag { get; private set; }
    internal PlayerSignals ReceivedSignals { get; private set; }
    internal PlayerStates ReceivedStates { get; private set; }

    protected override void OnPostInit(
        PlayerOrchestrator orchestrator,
        bool isMultiplayer,
        bool isOwner)
    {
        InitializeCount++;
        ReceivedMultiplayerFlag = isMultiplayer;
        ReceivedOwnerFlag = isOwner;
        ReceivedSignals = signals;
        ReceivedStates = states;
    }

    public void Cleanup()
    {
        CleanupCount++;
    }
}

internal sealed class PlayerNetworkInitializationProbe : PlayerNetworkComponent
{
    internal int InitializeCount { get; private set; }
    internal PlayerSignals ReceivedSignals { get; private set; }

    protected override void OnPostInit(PlayerOrchestrator orchestrator)
    {
        InitializeCount++;
        ReceivedSignals = signals;
    }
}

[Category("Baseline")]
public sealed class PlayerAndItemLogicTests
{
    private const string ProductionPlayerPrefabPath =
        "Assets/Collaborators/6aTowKa/Prefabs/Player.prefab";
    private const string ProductionEnemyPrefabPath =
        "Assets/Collaborators/Qewzdl/Prefabs/Entities/EnemyBase.prefab";
    private const string ProductionHidingPlacePrefabPath =
        "Assets/Collaborators/Qewzdl/Prefabs/Hiding Objects/Test Hiding Box.prefab";
    private const string ProductionHidingPlaceDataPath =
        "Assets/Collaborators/Qewzdl/Configs/Hiding/HidingPlaceData.asset";

    [Test]
    public void ProductionDraggablePrefabs_DeclareNavigationObstacle()
    {
        string[] prefabGuids = AssetDatabase.FindAssets(
            "t:Prefab",
            new[] { "Assets/Collaborators/6aTowKa/Prefabs" });
        int draggablePrefabCount = 0;

        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            DraggableObject draggable =
                prefab != null ? prefab.GetComponent<DraggableObject>() : null;

            if (draggable == null)
            {
                continue;
            }

            draggablePrefabCount++;
            Assert.That(
                prefab.GetComponent<ItemNavigationObstacle>(),
                Is.Not.Null,
                $"Draggable prefab '{path}' has no navigation adapter.");
            Assert.That(
                prefab.GetComponent<UnityEngine.AI.NavMeshObstacle>(),
                Is.Not.Null,
                $"Draggable prefab '{path}' has no NavMeshObstacle.");
        }

        Assert.That(
            draggablePrefabCount,
            Is.GreaterThan(0),
            "No production draggable prefabs were found.");
    }

    [Test]
    public void ProductionEnemy_ContainsServerItemPusher()
    {
        GameObject enemyPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductionEnemyPrefabPath
            );

        Assert.That(enemyPrefab, Is.Not.Null);

        EnemyItemPusher pusher = enemyPrefab.GetComponent<EnemyItemPusher>();

        Assert.That(pusher, Is.Not.Null);

        SerializedObject pusherObject = new(pusher);

        CapsuleCollider bodyCollider =
            pusherObject.FindProperty("bodyCollider").objectReferenceValue
                as CapsuleCollider;

        Assert.That(bodyCollider, Is.Not.Null);
        Assert.That(
            bodyCollider,
            Is.SameAs(enemyPrefab.GetComponent<CapsuleCollider>()),
            "The physical capsule must be on the Rigidbody root."
        );
        Assert.That(bodyCollider.transform, Is.SameAs(enemyPrefab.transform));
        Assert.That(bodyCollider.center, Is.EqualTo(Vector3.up));

        Collider[] solidColliders = enemyPrefab
            .GetComponentsInChildren<Collider>(true)
            .Where(collider => !collider.isTrigger)
            .ToArray();

        Assert.That(
            solidColliders,
            Does.Contain(bodyCollider),
            "The body capsule must be part of the compound Rigidbody."
        );

        // Deliberate root colliders are allowed - the shin box is what lets the
        // enemy push floor-level items. The invariant is about where colliders
        // live, not how many: anything on a child moves independently of the
        // Rigidbody root and corrupts the compound collider.
        Assert.That(
            solidColliders
                .Where(collider => collider.transform != enemyPrefab.transform)
                .ToArray(),
            Is.Empty,
            "Decorative child colliders must not join the enemy compound Rigidbody."
        );
        Assert.That(
            pusherObject.FindProperty("pushableLayers").intValue,
            Is.Not.Zero
        );

        EnemyPhysicsMotor physicsMotor =
            enemyPrefab.GetComponent<EnemyPhysicsMotor>();
        Rigidbody body = enemyPrefab.GetComponent<Rigidbody>();

        Assert.That(physicsMotor, Is.Not.Null);
        Assert.That(body, Is.Not.Null);
        Assert.That(body.isKinematic, Is.True);
        Assert.That(body.interpolation, Is.EqualTo(RigidbodyInterpolation.None));

        SerializedObject motorObject = new(physicsMotor);

        Assert.That(
            motorObject.FindProperty("networkObject").objectReferenceValue,
            Is.SameAs(enemyPrefab.GetComponent<NetworkObject>())
        );
        Assert.That(
            motorObject.FindProperty("agent").objectReferenceValue,
            Is.SameAs(enemyPrefab.GetComponent<NavMeshAgent>())
        );
        Assert.That(
            motorObject.FindProperty("body").objectReferenceValue,
            Is.SameAs(body)
        );
        Assert.That(
            motorObject.FindProperty("mass").floatValue,
            Is.GreaterThan(0f)
        );
    }

    [Test]
    public void ProductionPlayerPrefab_ContainsHidingRuntimeAndScopeBinding()
    {
        GameObject playerPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductionPlayerPrefabPath
            );

        Assert.That(playerPrefab, Is.Not.Null);

        PlayerHidingController hidingController =
            playerPrefab.GetComponent<PlayerHidingController>();
        PlayerInteraction interaction =
            playerPrefab.GetComponent<PlayerInteraction>();
        PlayerScopeLifetime scopeLifetime =
            playerPrefab.GetComponent<PlayerScopeLifetime>();
        PlayerEnemyAttackReceiver attackReceiver =
            playerPrefab.GetComponent<PlayerEnemyAttackReceiver>();
        PlayerHidingVignette hidingVignette =
            playerPrefab.GetComponent<PlayerHidingVignette>();
        PlayerActionGate actionGate =
            playerPrefab.GetComponent<PlayerActionGate>();

        Assert.That(hidingController, Is.Not.Null);
        Assert.That(interaction, Is.Not.Null);
        Assert.That(scopeLifetime, Is.Not.Null);
        Assert.That(attackReceiver, Is.Not.Null);
        Assert.That(hidingVignette, Is.Not.Null);
        Assert.That(actionGate, Is.Not.Null);
        Assert.That(
            hidingController,
            Is.InstanceOf<IPlayerHidingCommandService>()
        );
        Assert.That(
            attackReceiver,
            Is.InstanceOf<IHidingEntryEligibility>()
        );

        SerializedObject hidingObject = new(hidingController);
        SerializedObject interactionObject = new(interaction);
        SerializedObject scopeObject = new(scopeLifetime);

        Assert.That(
            hidingObject
                .FindProperty("bodyCollider")
                .objectReferenceValue,
            Is.Not.Null
        );
        Assert.That(
            hidingObject
                .FindProperty("cameraLook")
                .objectReferenceValue,
            Is.Not.Null
        );
        Assert.That(
            hidingObject
                .FindProperty("hidingVignette")
                .objectReferenceValue,
            Is.SameAs(hidingVignette)
        );
        Assert.That(
            hidingObject
                .FindProperty("playerActionGateSource")
                .objectReferenceValue,
            Is.SameAs(actionGate)
        );
        Assert.That(
            hidingObject
                .FindProperty("visualRoot")
                .objectReferenceValue,
            Is.Not.Null
        );
        Assert.That(
            hidingObject
                .FindProperty("gameplayColliders")
                .arraySize,
            Is.GreaterThan(0)
        );
        Assert.That(
            hidingObject
                .FindProperty("hitboxColliders")
                .arraySize,
            Is.Zero
        );
        Assert.That(
            interactionObject
                .FindProperty("playerHidingCommandSource")
                .objectReferenceValue,
            Is.SameAs(hidingController)
        );
        Assert.That(
            interactionObject
                .FindProperty("playerActionGateSource")
                .objectReferenceValue,
            Is.SameAs(actionGate)
        );
        Assert.That(
            scopeObject
                .FindProperty("actionGateService")
                .objectReferenceValue,
            Is.SameAs(actionGate)
        );
        Assert.That(
            scopeObject
                .FindProperty("hidingStateService")
                .objectReferenceValue,
            Is.SameAs(hidingController)
        );
    }

    [Test]
    public void ProductionHidingPlace_HasFailClosedSafetyConfiguration()
    {
        GameObject hidingPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductionHidingPlacePrefabPath
            );
        HidingPlaceData hidingData =
            AssetDatabase.LoadAssetAtPath<HidingPlaceData>(
                ProductionHidingPlaceDataPath
            );

        Assert.That(hidingPrefab, Is.Not.Null);
        Assert.That(hidingData, Is.Not.Null);
        Assert.That(
            hidingPrefab.layer,
            Is.EqualTo(LayerMask.NameToLayer("Interactable"))
        );
        Assert.That(
            hidingPrefab.GetComponent<Unity.Netcode.NetworkObject>(),
            Is.Not.Null
        );
        Assert.That(
            hidingPrefab.GetComponent<NetworkHidingGameplayNoiseEmitter>(),
            Is.Not.Null
        );
        Assert.That(
            hidingPrefab.GetComponent<HidingPlacePresentation>(),
            Is.Not.Null
        );
        Assert.That(
            hidingPrefab.GetComponent<HidingPlaceNavigationObstacle>(),
            Is.Not.Null
        );
        UnityEngine.AI.NavMeshObstacle navigationObstacle =
            hidingPrefab.GetComponent<UnityEngine.AI.NavMeshObstacle>();
        Assert.That(navigationObstacle, Is.Not.Null);
        Assert.That(navigationObstacle.carving, Is.True);
        Assert.That(navigationObstacle.carveOnlyStationary, Is.True);

        HidingPlaceInteractable hidingPlace =
            hidingPrefab.GetComponent<HidingPlaceInteractable>();

        Assert.That(hidingPlace, Is.Not.Null);

        SerializedObject hidingPlaceObject = new(hidingPlace);

        Assert.That(
            hidingPlaceObject
                .FindProperty("data")
                .objectReferenceValue,
            Is.SameAs(hidingData)
        );
        Assert.That(
            hidingPlaceObject
                .FindProperty("interactionAnchor")
                .objectReferenceValue,
            Is.Not.Null
        );
        Assert.That(
            hidingPlaceObject
                .FindProperty("hidingPoint")
                .objectReferenceValue,
            Is.Not.Null
        );
        Assert.That(
            hidingPlaceObject
                .FindProperty("cameraAnchor")
                .objectReferenceValue,
            Is.Not.Null
        );
        Assert.That(
            hidingPlaceObject
                .FindProperty("exitPoint")
                .objectReferenceValue,
            Is.Not.Null
        );
        SerializedProperty fallbackExitPoints =
            hidingPlaceObject.FindProperty("fallbackExitPoints");
        Assert.That(fallbackExitPoints, Is.Not.Null);
        Assert.That(
            fallbackExitPoints.arraySize,
            Is.GreaterThanOrEqualTo(2),
            "The production hiding prefab must provide alternative exits."
        );
        for (int i = 0; i < fallbackExitPoints.arraySize; i++)
        {
            Assert.That(
                fallbackExitPoints
                    .GetArrayElementAtIndex(i)
                    .objectReferenceValue,
                Is.Not.Null
            );
        }

        Assert.That(hidingData.RequireEntryLineOfSight, Is.True);
        Assert.That(
            hidingData.EntryLineOfSightBlockingMask.value,
            Is.Not.Zero
        );
        Assert.That(
            hidingData.ExitObstructionMask.value,
            Is.Not.Zero
        );
        Assert.That(
            hidingData.ExitCollisionSkin,
            Is.GreaterThanOrEqualTo(0f)
        );
        Assert.That(hidingData.EnterDuration, Is.GreaterThanOrEqualTo(0f));
        Assert.That(hidingData.ExitDuration, Is.GreaterThanOrEqualTo(0f));
        Assert.That(
            hidingData.MinimumCameraYaw,
            Is.LessThanOrEqualTo(0f)
        );
        Assert.That(
            hidingData.MaximumCameraYaw,
            Is.GreaterThanOrEqualTo(0f)
        );
        Assert.That(
            hidingData.MinimumCameraPitch,
            Is.LessThanOrEqualTo(0f)
        );
        Assert.That(
            hidingData.MaximumCameraPitch,
            Is.GreaterThanOrEqualTo(0f)
        );
        Assert.That(hidingData.ShowHidingVignette, Is.True);
        Assert.That(
            hidingData.HidingVignetteOpacity,
            Is.InRange(0f, 1f)
        );
        Assert.That(
            hidingData.HidingVignetteInnerRadius,
            Is.InRange(0f, 0.95f)
        );
        Assert.That(
            hidingData.HidingVignetteFadeDuration,
            Is.GreaterThanOrEqualTo(0f)
        );
        Assert.That(hidingData.EnterNoiseRadius, Is.GreaterThan(0f));
        Assert.That(hidingData.EnterNoiseLoudness, Is.GreaterThan(0f));
        Assert.That(hidingData.ExitNoiseRadius, Is.GreaterThan(0f));
        Assert.That(hidingData.ExitNoiseLoudness, Is.GreaterThan(0f));
        Assert.That(hidingData.EnemiesCanInvestigate, Is.True);
        Assert.That(
            hidingData.EnemyInvestigationDistance,
            Is.GreaterThan(0f)
        );
    }

    // Four rules about a winded chest, none of which anybody can hear break.
    //
    // Coughs and second exhales are rare on purpose, so losing one of these in
    // a refactor produces a recording somebody would have to sit through for
    // several minutes to doubt. That is exactly the kind of rule worth writing
    // down rather than listening for.
    [Test]
    public void BreathRhythm_KeepsTheRulesOfAWindedChest()
    {
        // Everything certain, so the only thing deciding the order is the
        // order itself.
        BreathRhythm.Chances always = new(1f, 1f, 1f);
        BreathRhythm rhythm = new();

        Assert.That(rhythm.Current, Is.EqualTo(BreathRhythm.Step.Inhale),
            "A spell of breathing has to start by breathing in.");

        // A cough cannot follow an inhale however certain the rolls, because
        // the air has to be going out for there to be a cough at all.
        rhythm.Advance(always, coughAllowed: true, () => 0f);

        Assert.That(rhythm.Current, Is.EqualTo(BreathRhythm.Step.Exhale),
            "Something other than an exhale followed an inhale - a cough can " +
            "only come out of air already on its way out.");

        // Two coughs, and then no more: a third stops being tiredness and
        // starts being a condition.
        rhythm.Advance(always, coughAllowed: true, () => 0f);
        Assert.That(rhythm.Current, Is.EqualTo(BreathRhythm.Step.Cough));

        rhythm.Advance(always, coughAllowed: true, () => 0f);
        Assert.That(rhythm.Current, Is.EqualTo(BreathRhythm.Step.Cough));
        Assert.That(rhythm.IsRepeat, Is.True,
            "The second cough was not marked as a repeat, so it will be " +
            "spaced like a first one.");

        rhythm.Advance(always, coughAllowed: true, () => 0f);
        Assert.That(rhythm.Current, Is.EqualTo(BreathRhythm.Step.Inhale),
            "A third cough in a row. Two is a chest; three is a chest " +
            "infection, and the player has only been running.");

        // The same for exhales, with coughing refused so the exhale branch is
        // the only one left.
        rhythm = new BreathRhythm();
        rhythm.Advance(always, coughAllowed: false, () => 0f);
        rhythm.Advance(always, coughAllowed: false, () => 0f);

        Assert.That(rhythm.Current, Is.EqualTo(BreathRhythm.Step.Exhale));
        Assert.That(rhythm.IsRepeat, Is.True);

        rhythm.Advance(always, coughAllowed: false, () => 0f);
        Assert.That(rhythm.Current, Is.EqualTo(BreathRhythm.Step.Inhale),
            "A third exhale in a row.");

        // And the gate. Refused, a cough is impossible no matter how the dice
        // land - that is what makes it a gate rather than another chance.
        rhythm = new BreathRhythm();

        for (int step = 0; step < 50; step++)
        {
            rhythm.Advance(always, coughAllowed: false, () => 0f);

            Assert.That(
                rhythm.Current,
                Is.Not.EqualTo(BreathRhythm.Step.Cough),
                "A cough happened while coughing was refused, so a player who " +
                "jogged to a door can be heard hacking at it.");
        }
    }

    // Setting off is heard, and then every stride after it.
    //
    // The first half of that was a bug somebody reported by ear: a player
    // walked a noticeable distance in silence before the first footstep
    // arrived, because the count started from zero every time and the
    // acceleration up to walking pace was silent on top of it. The fix is easy
    // to lose in a refactor and hard to hear yourself back into, so it is
    // written down here instead.
    [Test]
    public void StrideCounter_LandsAFootWhenMovementStartsAndEveryStrideAfter()
    {
        const float Stride = 0.9f;
        StrideCounter counter = new();

        Assert.That(
            counter.Advance(true, 0.001f, Stride),
            Is.True,
            "Setting off did not land a foot, so a player walks away in " +
            "silence until a whole stride has gone by.");

        Assert.That(
            counter.Advance(true, Stride * 0.5f, Stride),
            Is.False,
            "A second step landed half a stride after the first.");

        Assert.That(
            counter.Advance(true, Stride * 0.5f, Stride),
            Is.True,
            "No step landed after a full stride of walking.");

        // Stopping forgets the part-stride. Creeping half a stride and then
        // standing up to walk must not pay for that crouched half with a step
        // on the first loud frame - that is a footstep for ground already
        // crossed quietly.
        Assert.That(counter.Advance(true, Stride * 0.9f, Stride), Is.False);
        Assert.That(counter.Advance(false, 0f, Stride), Is.False);

        Assert.That(
            counter.Advance(true, 0.001f, Stride),
            Is.True,
            "Moving again did not land a foot.");

        Assert.That(
            counter.Advance(true, Stride * 0.5f, Stride),
            Is.False,
            "The part-stride from before the stop was still being counted.");
    }

    // A player who walked cannot claim to be out of breath.
    //
    // Breathing is the one noise the client asks for rather than the server
    // deriving - the breath and the sound of it have to be the same instant,
    // or a player hears themselves cough twice. What keeps that honest is this
    // model, which works out how hard somebody has been working from nothing
    // but where they have been.
    //
    // Two ways for it to be wrong, and both look identical from outside the
    // game: draining on any movement lets a walker be heard gasping, and
    // draining on none refuses every breath in the match. So the assertions
    // are about which gaits cost something, not about the exact level left.
    [Test]
    public void ObservedExertion_OnlyRunningCostsAnything()
    {
        PlayerMovementProfile profile =
            ScriptableObject.CreateInstance<PlayerMovementProfile>();

        try
        {
            TestReflection.SetField(profile, "walkSpeed", 2.5f);
            TestReflection.SetField(profile, "runSpeedMultiplier", 1.5f);
            TestReflection.SetField(profile, "crouchSpeedMultiplier", 0.5f);
            TestReflection.SetField(profile, "runSeconds", 8f);
            TestReflection.SetField(profile, "recoverySeconds", 14f);

            Assert.That(
                Travel(profile, profile.WalkSpeed, 30f), Is.EqualTo(1f),
                "Half a minute of walking emptied a tank, so a player who " +
                "never ran could be heard gasping.");

            Assert.That(
                Travel(profile, profile.CrouchSpeed, 30f), Is.EqualTo(1f),
                "Crouching emptied a tank.");

            Assert.That(
                Travel(profile, 0f, 30f), Is.EqualTo(1f),
                "Standing still emptied a tank.");

            float afterRunning = Travel(profile, profile.RunSpeed, 4f);

            Assert.That(
                afterRunning, Is.LessThan(1f),
                "Four seconds of running cost nothing, so nobody could ever " +
                "be heard breathing at all.");

            // Roughly half of an eight second tank, and asserted loosely: what
            // matters is that it drains at about the rate the legs drain at,
            // not that two models agree to the frame.
            Assert.That(afterRunning, Is.EqualTo(0.5f).Within(0.05f));
        }
        finally
        {
            Object.DestroyImmediate(profile);
        }
    }

    // Walks a body in a straight line at one speed and hands back what the
    // observer believes is left. Sampled at sixty a second, because that is
    // roughly what it will see in a game and a model that only works at large
    // steps is not one worth having.
    private static float Travel(
        PlayerMovementProfile profile,
        float speed,
        float seconds)
    {
        ObservedExertion exertion = new();

        const float Step = 1f / 60f;
        int steps = Mathf.RoundToInt(seconds / Step);

        for (int i = 0; i <= steps; i++)
        {
            exertion.Sample(
                new Vector3(speed * i * Step, 0f, 0f),
                i * Step,
                profile);
        }

        return exertion.Stamina;
    }

    // Four rests, four numbers, and a switch between them that is one typo
    // away from handing crouching the walking rate.
    //
    // Proved by giving the four fields values nothing else could produce, so a
    // swapped branch cannot pass by landing on a number that happens to match.
    // The ordering of the shipped values is deliberately not asserted - that is
    // tuning, and a test that freezes tuning is a test somebody deletes.
    [Test]
    public void MovementProfile_RecoveryRateComesFromTheRightRest()
    {
        PlayerMovementProfile profile =
            ScriptableObject.CreateInstance<PlayerMovementProfile>();

        try
        {
            TestReflection.SetField(profile, "recoveryStanding", 0.11f);
            TestReflection.SetField(profile, "recoveryWalking", 0.22f);
            TestReflection.SetField(profile, "recoveryCrouchingStill", 0.33f);
            TestReflection.SetField(profile, "recoveryCrouchWalking", 0.44f);

            Assert.That(profile.RecoveryRateFor(false, false), Is.EqualTo(0.11f).Within(0.0001f),
                "Standing still did not read the standing rate.");
            Assert.That(profile.RecoveryRateFor(false, true), Is.EqualTo(0.22f).Within(0.0001f),
                "Walking did not read the walking rate.");
            Assert.That(profile.RecoveryRateFor(true, false), Is.EqualTo(0.33f).Within(0.0001f),
                "Crouching still did not read the crouching-still rate.");
            Assert.That(profile.RecoveryRateFor(true, true), Is.EqualTo(0.44f).Within(0.0001f),
                "Crouch walking did not read the crouch-walking rate.");
        }
        finally
        {
            Object.DestroyImmediate(profile);
        }
    }

    // Being tired is allowed to cost speed and is not allowed to buy any.
    //
    // The scale is a lerp with a threshold, and a lerp with its ends the wrong
    // way round still returns plausible numbers - it just returns them for the
    // wrong stamina, which on a screen with no stamina bar is invisible. So
    // the direction is asserted rather than the values: nothing below the
    // threshold may be faster than being above it, and nothing anywhere may
    // exceed a full tank.
    [Test]
    public void MovementProfile_TirednessOnlyEverCostsSpeed()
    {
        PlayerMovementProfile profile =
            ScriptableObject.CreateInstance<PlayerMovementProfile>();

        try
        {
            TestReflection.SetField(profile, "tiredBelow", 0.4f);
            TestReflection.SetField(profile, "exhaustedOverallScale", 0.5f);

            Assert.That(profile.OverallScaleAtStamina(1f), Is.EqualTo(1f),
                "A full tank is not tired.");
            Assert.That(profile.OverallScaleAtStamina(0.4f), Is.EqualTo(1f),
                "The threshold itself is the last place that is not tired.");
            Assert.That(profile.OverallScaleAtStamina(0f), Is.EqualTo(0.5f).Within(0.0001f),
                "An empty tank is not at the exhausted scale.");

            float previous = float.PositiveInfinity;

            for (int step = 20; step >= 0; step--)
            {
                float stamina = step / 20f;
                float scale = profile.OverallScaleAtStamina(stamina);

                Assert.That(scale, Is.LessThanOrEqualTo(1f),
                    $"At {stamina:0.00} of a tank tiredness made the player faster than fresh.");

                Assert.That(scale, Is.LessThanOrEqualTo(previous + 0.0001f),
                    $"At {stamina:0.00} of a tank the player sped up as the tank emptied.");

                previous = scale;
            }

            // And the same on the way out, through the thing the controller
            // actually calls - a gait scale that forgot to include tiredness
            // would pass everything above and still ship the bug.
            foreach (bool running in new[] { false, true })
            {
                Assert.That(
                    profile.TotalScaleFor(false, running, 0f),
                    Is.LessThan(profile.TotalScaleFor(false, running, 1f)),
                    running
                        ? "An exhausted run is not slower than a fresh one."
                        : "An exhausted walk is not slower than a fresh one.");
            }
        }
        finally
        {
            Object.DestroyImmediate(profile);
        }
    }

    // The tank has to be able to leave empty.
    //
    // The recovery curve multiplies the fill rate and is read at the current
    // level, so a curve touching zero at its left end makes empty a state
    // nothing can escape: no stamina, no recovery, no stamina. That shipped,
    // and it took playing the game to find - a player stuck at zero for the
    // rest of the match, with no error anywhere and every test passing.
    //
    // The shape that does it is the obvious one. "Slow at first" is drawn from
    // the bottom-left corner, which is exactly zero.
    //
    // Asserted as a property of the curve rather than by running a fill loop:
    // a loop here would be a second copy of the controller's arithmetic, free
    // to agree with a version of that formula which no longer exists. What
    // actually matters is that the multiplier is never zero anywhere, because
    // that is what makes any fill loop terminate.
    [Test]
    public void MovementProfile_RecoveryCurveNeverStopsTheTankRefilling()
    {
        PlayerMovementProfile profile =
            ScriptableObject.CreateInstance<PlayerMovementProfile>();

        try
        {
            TestReflection.SetField(
                profile,
                "recoveryCurve",
                AnimationCurve.Linear(0f, 0f, 1f, 1f));

            Assert.That(
                profile.RecoveryCurveStrandsAtEmpty(),
                Is.True,
                "A curve drawn to zero at the empty end has to be reported as " +
                "one, or the floor silently replaces a shape somebody drew " +
                "and nobody is told.");

            for (int step = 0; step <= 20; step++)
            {
                float stamina = step / 20f;

                Assert.That(
                    profile.RecoveryCurveAt(stamina),
                    Is.GreaterThan(0f),
                    $"At {stamina:0.00} of a tank the curve fills it at no " +
                    "rate at all, so it can never leave that level.");
            }

            // A curve nobody drew is an absence of opinion, not a zero.
            TestReflection.SetField(profile, "recoveryCurve", new AnimationCurve());

            Assert.That(profile.RecoveryCurveAt(0f), Is.EqualTo(1f));
            Assert.That(profile.RecoveryCurveStrandsAtEmpty(), Is.False);

            // And a flat one is still exactly itself: the floor is a floor,
            // not a correction applied to every curve on the way past.
            TestReflection.SetField(
                profile,
                "recoveryCurve",
                AnimationCurve.Constant(0f, 1f, 0.5f));

            Assert.That(profile.RecoveryCurveAt(0f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(profile.RecoveryCurveStrandsAtEmpty(), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(profile);
        }
    }

    [Test]
    public void PlayerSignals_RegisterUniqueNamedSignalsAndDispatchListeners()
    {
        PlayerSignals signals = new();

        // Ten since running arrived. The count is hard-coded rather than
        // derived on purpose: a signal added without a thought lands here
        // before it lands in a build, which is the only moment anybody is
        // going to ask whether it needed to exist.
        Assert.That(signals.SignalsList.Count, Is.EqualTo(10));
        Assert.That(
            signals.SignalsList.Cast<BasePlayerSignal>().Select(signal => signal.DebugName),
            Is.Unique);

        int triggerCount = 0;
        Vector2 lastMove = default;

        void HandleInteract()
        {
            triggerCount++;
        }

        void HandleMove(Vector2 movement)
        {
            lastMove = movement;
        }

        signals.Interact.Listen(HandleInteract);
        signals.MoveSignal.Listen(HandleMove);

        signals.Interact.Trigger();
        signals.MoveSignal.Trigger(new Vector2(2f, 3f));

        Assert.That(triggerCount, Is.EqualTo(1));
        Assert.That(lastMove, Is.EqualTo(new Vector2(2f, 3f)));
        Assert.That(signals.Interact.GetListeners().Length, Is.EqualTo(1));

        signals.Interact.Unlisten(HandleInteract);
        signals.MoveSignal.Unlisten(HandleMove);

        Assert.That(signals.Interact.GetListeners(), Is.Null);
        Assert.That(signals.MoveSignal.GetListeners(), Is.Null);
    }

    [Test]
    public void PlayerOrchestrator_SetupInitializesOnlyEnabledComponents()
    {
        GameObject playerObject = new("Player orchestrator test");

        try
        {
            PlayerOrchestrator orchestrator =
                playerObject.AddComponent<PlayerOrchestrator>();
            PlayerComponentInitializationProbe enabledProbe =
                playerObject.AddComponent<PlayerComponentInitializationProbe>();
            PlayerComponentInitializationProbe disabledProbe =
                playerObject.AddComponent<PlayerComponentInitializationProbe>();
            PlayerNetworkInitializationProbe networkProbe =
                playerObject.AddComponent<PlayerNetworkInitializationProbe>();
            disabledProbe.enabled = false;

            orchestrator.Setup(isMultiplayer: true, isOwner: false);

            Assert.That(enabledProbe.InitializeCount, Is.EqualTo(1));
            Assert.That(enabledProbe.ReceivedMultiplayerFlag, Is.True);
            Assert.That(enabledProbe.ReceivedOwnerFlag, Is.False);
            Assert.That(enabledProbe.ReceivedSignals, Is.SameAs(orchestrator.Signals));
            Assert.That(enabledProbe.ReceivedStates, Is.SameAs(orchestrator.States));
            Assert.That(disabledProbe.InitializeCount, Is.Zero);
            Assert.That(networkProbe.InitializeCount, Is.EqualTo(1));
            Assert.That(networkProbe.ReceivedSignals, Is.SameAs(orchestrator.Signals));
        }
        finally
        {
            Object.DestroyImmediate(playerObject);
        }
    }

    [Test]
    public void PlayerStates_KeepInteractionStatesIndependent()
    {
        PlayerStates states = new();

        states.IsDragging = true;
        Assert.That(states.IsDragging, Is.True);
        Assert.That(states.IsCarrying, Is.False);

        states.IsCarrying = true;
        states.IsDragging = false;

        Assert.That(states.IsDragging, Is.False);
        Assert.That(states.IsCarrying, Is.True);
        Assert.That(states.IsHiding, Is.False);

        states.IsHiding = true;
        states.IsCarrying = false;

        Assert.That(states.IsHiding, Is.True);
        Assert.That(states.IsDragging, Is.False);
        Assert.That(states.IsCarrying, Is.False);
    }

    [Test]
    public void CameraLook_HidingView_AnchorsAndRestoresLocalPose()
    {
        GameObject player = new("Hiding camera player");
        GameObject cameraPivot = new("Hiding camera pivot");
        GameObject anchor = new("Hiding camera anchor");
        player.SetActive(false);

        try
        {
            player.AddComponent<Rigidbody>().useGravity = false;
            cameraPivot.transform.SetParent(player.transform, false);
            cameraPivot.transform.localPosition =
                new Vector3(0f, 1.6f, 0f);

            CameraLook cameraLook =
                cameraPivot.AddComponent<CameraLook>();
            SerializedObject cameraLookObject = new(cameraLook);
            cameraLookObject
                .FindProperty("playerTransform")
                .objectReferenceValue = player.transform;
            cameraLookObject.ApplyModifiedPropertiesWithoutUndo();

            Vector3 returnLocalPosition =
                cameraPivot.transform.localPosition;
            anchor.transform.SetPositionAndRotation(
                new Vector3(4f, 2f, -3f),
                Quaternion.Euler(0f, 90f, 0f)
            );

            player.SetActive(true);
            cameraLook.SetLocalControl(true);

            Assert.That(
                cameraLook.TrySetHidingView(
                    anchor.transform,
                    -40f,
                    40f,
                    -25f,
                    30f,
                    allowPeeking: true
                ),
                Is.True
            );
            Assert.That(cameraLook.IsHidingViewActive, Is.True);
            Assert.That(
                Vector3.Distance(
                    cameraPivot.transform.position,
                    anchor.transform.position
                ),
                Is.LessThan(0.001f)
            );

            cameraLook.ClearHidingView();

            Assert.That(cameraLook.IsHidingViewActive, Is.False);
            Assert.That(
                Vector3.Distance(
                    cameraPivot.transform.localPosition,
                    returnLocalPosition
                ),
                Is.LessThan(0.001f)
            );
        }
        finally
        {
            Object.DestroyImmediate(anchor);
            Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void ViewmodelEntry_RoundTripsTransformPoseAndScale()
    {
        GameObject sourceObject = new("Viewmodel source");
        GameObject targetObject = new("Viewmodel target");

        try
        {
            sourceObject.transform.localPosition = new Vector3(1f, 2f, 3f);
            sourceObject.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
            sourceObject.transform.localScale = new Vector3(2f, 3f, 4f);

            ViewmodelItemEntry entry = new();
            entry.SetFrom(sourceObject.transform);
            entry.ApplyTo(targetObject.transform);

            Assert.That(
                targetObject.transform.localPosition,
                Is.EqualTo(sourceObject.transform.localPosition));
            Assert.That(
                Quaternion.Angle(
                    targetObject.transform.localRotation,
                    sourceObject.transform.localRotation),
                Is.LessThan(0.001f));
            Assert.That(
                targetObject.transform.localScale,
                Is.EqualTo(sourceObject.transform.localScale));
        }
        finally
        {
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(targetObject);
        }
    }
}
