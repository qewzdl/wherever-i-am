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
        "Assets/Collaborators/Qewzdl/Prefabs/Entities/Enemy.prefab";
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
            hidingObject
                .FindProperty("localViewmodelRoot")
                .objectReferenceValue,
            Is.Not.Null
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

    [Test]
    public void PlayerSignals_RegisterUniqueNamedSignalsAndDispatchListeners()
    {
        PlayerSignals signals = new();

        Assert.That(signals.SignalsList.Count, Is.EqualTo(9));
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
