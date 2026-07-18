#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

internal sealed class BakedNavMeshAttackEffectProbe : IEnemyAttackEffect
{
    internal int ApplyCount { get; private set; }

    public bool TryApply(EnemyAttackContext context)
    {
        ApplyCount++;
        return true;
    }
}

[Category("Gameplay")]
public sealed class EnemyBakedNavMeshPlayModeTests
{
    private const string EnemyPrefabPath =
        "Assets/Collaborators/Qewzdl/Prefabs/Entities/Enemy.prefab";
    private const string EnemyConfigPath =
        "Assets/Collaborators/Qewzdl/Configs/Enemies/Enemy Configs/Granny/GrannyEnemyConfig.asset";
    private const float TimeoutSeconds = 12f;
    private const int EnvironmentLayer = 7;
    private const int PlayerLayer = 3;

    private readonly List<UnityEngine.Object> cleanup = new();
    private readonly List<NavMeshSurface> surfaces = new();

    private NetworkManager manager;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (manager != null && manager.IsListening)
            manager.Shutdown(discardMessageQueue: true);

        float timeout = Time.realtimeSinceStartup + TimeoutSeconds;

        while (manager != null &&
               (manager.IsListening || manager.ShutdownInProgress) &&
               Time.realtimeSinceStartup < timeout)
        {
            yield return null;
        }

        for (int i = surfaces.Count - 1; i >= 0; i--)
        {
            if (surfaces[i] != null)
                surfaces[i].RemoveData();
        }

        surfaces.Clear();

        for (int i = cleanup.Count - 1; i >= 0; i--)
        {
            if (cleanup[i] != null)
                UnityEngine.Object.DestroyImmediate(cleanup[i]);
        }

        cleanup.Clear();
        manager = null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator ServerEnemy_OnPrebuiltNavMesh_HandlesNoiseVisionChaseLossAndBlockedAttack()
    {
        yield return StartHost();

        GameObject enemyPrefab = LoadRequiredAsset<GameObject>(EnemyPrefabPath);
        EnemyConfig config = LoadRequiredAsset<EnemyConfig>(EnemyConfigPath);
        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        GameplayNoiseWorldService noiseWorld = CreateNoiseWorld();
        EnemyTarget target = CreateSpawnedTarget(
            new Vector3(0f, 0f, 1.2f),
            canBeDetected: false);

        BakedNavMeshAttackEffectProbe attackEffect = new();
        EnemyAttackPipeline attackPipeline = new(
            attackEffect,
            new EnemyAttackCooldown(),
            new EnemyAttackContextFactory(),
            new EnemyLineOfHitValidator(),
            consumeCooldownOnFailedEffect: false,
            target);
        List<EnemyAttackResultType> attackResults = new();
        attackPipeline.AttackResolved += result =>
            attackResults.Add(result.Type);

        GameObject attackBlocker = CreateGeometry(
            "Attack blocker",
            new Vector3(0f, 1f, 0.6f),
            new Vector3(2f, 2f, 0.2f));
        Physics.SyncTransforms();

        EnemyAttackResult attackStarted = attackPipeline.TryStartAttack(
            target,
            config,
            Vector3.zero,
            target);
        Assert.That(
            attackStarted.Type,
            Is.EqualTo(EnemyAttackResultType.Started));

        attackPipeline.Tick(1f, Vector3.zero);

        Assert.That(
            attackResults,
            Does.Contain(EnemyAttackResultType.LineOfHitBlocked));
        Assert.That(attackEffect.ApplyCount, Is.Zero);

        attackBlocker.SetActive(false);
        target.transform.position = new Vector3(0f, 0f, 5f);
        Physics.SyncTransforms();

        NetworkEnemyController enemy = CreateSpawnedProductionEnemy(
            enemyPrefab,
            noiseWorld,
            new Vector3(0f, 0f, -5f));
        EnemyServerRuntime runtime =
            enemy.GetComponent<EnemyServerRuntime>();
        NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();

        yield return WaitForCondition(
            () => runtime.IsRunning && agent.enabled && agent.isOnNavMesh,
            "Production enemy did not start on the prebuilt NavMesh.");

        Vector3 noisePosition = new Vector3(3f, 0f, -1f);
        float distanceBeforeNoise =
            Vector3.Distance(enemy.transform.position, noisePosition);

        Assert.That(
            noiseWorld.TryRaiseNoiseServer(
                noisePosition,
                20f,
                1f,
                GameplayNoiseSourceType.Item),
            Is.True);

        yield return WaitForCondition(
            () => enemy.CurrentState == EnemyState.Investigate,
            "Enemy did not investigate an authoritative gameplay noise.");
        yield return WaitForCondition(
            () => Vector3.Distance(enemy.transform.position, noisePosition) <
                  distanceBeforeNoise - 0.5f,
            "Enemy did not navigate toward the heard noise.");

        PlayModeTestReflection.SetField(target, "canBeDetected", true);
        target.transform.position =
            enemy.transform.position + enemy.transform.forward * 7f;
        Physics.SyncTransforms();

        Vector3 chaseStart = enemy.transform.position;
        yield return WaitForCondition(
            () => enemy.HasTarget &&
                  (enemy.CurrentState == EnemyState.Chase ||
                   enemy.CurrentState == EnemyState.Attack),
            "Enemy did not acquire a visible spawned player target.");
        yield return WaitForCondition(
            () => Vector3.Distance(enemy.transform.position, chaseStart) > 0.5f ||
                  enemy.CurrentState == EnemyState.Attack,
            "Enemy acquired the target but did not chase it on NavMesh.");

        target.transform.position =
            enemy.transform.position + Vector3.forward * 60f;
        Physics.SyncTransforms();

        yield return WaitForCondition(
            () => !enemy.HasTarget &&
                  enemy.CurrentState == EnemyState.Investigate,
            "Enemy did not lose the distant target and preserve investigation state.");
    }

    [UnityTest]
    public IEnumerator Navigator_OnPrebuiltNavMesh_OpensBlockingDoorBeforeContinuing()
    {
        GameObject enemyPrefab = LoadRequiredAsset<GameObject>(EnemyPrefabPath);
        EnemyConfig config = LoadRequiredAsset<EnemyConfig>(EnemyConfigPath);
        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        EnemyDoorInteractionConfig doorConfig =
            Track(ScriptableObject.CreateInstance<EnemyDoorInteractionConfig>());
        PlayModeTestReflection.SetField(
            doorConfig,
            "interactionDuration",
            0.05f);
        PlayModeTestReflection.SetField(
            doorConfig,
            "waitAfterInteractionDuration",
            0.05f);
        PlayModeTestReflection.SetField(doorConfig, "detectionRadius", 3f);
        PlayModeTestReflection.SetField(
            doorConfig,
            "interactionDistance",
            1.25f);
        PlayModeTestReflection.SetField(
            doorConfig,
            "usePhysicsOverlapFallback",
            false);

        DoorInteractableObject door = CreateEnemyDoor(
            Vector3.zero,
            out EnemyDoorInteractionZone doorZone);
        Assert.That(doorZone.IsBlockingNavigation, Is.True);

        GameObject actor = Track(new GameObject("Door navigation enemy"));
        actor.SetActive(false);
        actor.layer = 6;
        actor.transform.position = new Vector3(0f, 0f, -1f);

        NavMeshAgent agent = actor.AddComponent<NavMeshAgent>();
        agent.agentTypeID = standingAgentTypeId;
        agent.speed = 3f;
        agent.acceleration = 20f;
        EnemyDoorInteractor doorInteractor =
            actor.AddComponent<EnemyDoorInteractor>();
        PlayModeTestReflection.SetField(
            doorInteractor,
            "config",
            doorConfig);
        EnemyNavigator navigator = actor.AddComponent<EnemyNavigator>();
        actor.SetActive(true);

        Assert.That(
            TryPlaceAgent(agent, actor.transform.position),
            Is.True,
            "Test navigator could not be placed on the baked surface.");
        navigator.Configure(config);

        Vector3 destination = new Vector3(0f, 0f, 5f);
        Assert.That(navigator.TryMoveTo(destination, 3f), Is.True);
        Assert.That(door.IsReservedByEnemy, Is.True);
        Assert.That(agent.isStopped, Is.True);

        yield return WaitForCondition(
            () =>
            {
                navigator.TickNavigationGate();
                return door.IsOpen && !doorInteractor.HasActiveInteraction;
            },
            "Enemy did not complete the authoritative door interaction.");

        yield return WaitForCondition(
            () =>
            {
                navigator.TryMoveTo(destination, 3f);
                return actor.transform.position.z > 0.5f;
            },
            "Enemy did not continue along the baked path after opening the door.");

        Assert.That(door.IsOpen, Is.True);
        Assert.That(door.IsReservedByEnemy, Is.True);
    }

    [UnityTest]
    public IEnumerator Navigator_OnDualBakedNavMeshes_CrawlsUnderCeilingAndStandsAfterExit()
    {
        GameObject enemyPrefab = LoadRequiredAsset<GameObject>(EnemyPrefabPath);
        EnemyConfig sourceConfig =
            LoadRequiredAsset<EnemyConfig>(EnemyConfigPath);
        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeLowCeilingCorridor(
            standingAgentTypeId,
            crawlingAgentTypeId);

        EnemyConfig config = CloneNavigationConfig(sourceConfig);
        EnemyPostureConfig postureProfile = config.PostureProfile;
        postureProfile.standingToCrawlingTransitionDuration = 0f;
        postureProfile.crawlingToStandingTransitionDuration = 0f;
        postureProfile.minPostureDuration = 0f;
        postureProfile.standingRecoveryCheckInterval = 0.05f;

        GameObject actor = Track(new GameObject("Posture navigation enemy"));
        actor.SetActive(false);
        actor.layer = 6;
        actor.transform.position = new Vector3(0f, 0f, -6f);
        actor.AddComponent<NetworkObject>();

        NavMeshAgent agent = actor.AddComponent<NavMeshAgent>();
        agent.enabled = false;
        agent.agentTypeID = standingAgentTypeId;
        agent.speed = 4f;
        agent.acceleration = 30f;

        CapsuleCollider bodyCollider =
            actor.AddComponent<CapsuleCollider>();
        bodyCollider.height = postureProfile.standingBodyColliderHeight;
        bodyCollider.radius = postureProfile.standingBodyColliderRadius;
        bodyCollider.center = postureProfile.standingBodyColliderCenter;

        EnemyNetworkState networkState =
            actor.AddComponent<EnemyNetworkState>();
        EnemyPostureController postureController =
            actor.AddComponent<EnemyPostureController>();
        EnemyNavigator navigator = actor.AddComponent<EnemyNavigator>();

        PlayModeTestReflection.SetField(
            postureController,
            "standingAgentTypeId",
            standingAgentTypeId);
        PlayModeTestReflection.SetField(
            postureController,
            "crawlingAgentTypeId",
            crawlingAgentTypeId);
        actor.SetActive(true);

        agent.enabled = true;
        Assert.That(
            TryPlaceAgent(agent, actor.transform.position),
            Is.True,
            "Posture enemy could not be placed on the standing NavMesh.");
        Assert.That(
            postureController.TryInitializeServer(config, networkState),
            Is.True);
        navigator.Configure(config);

        Vector3 destination = new Vector3(0f, 0f, 6f);
        bool observedCrawling = false;

        yield return WaitForCondition(
            () =>
            {
                navigator.TryMoveTo(destination, config.chaseSpeed);
                observedCrawling |=
                    postureController.CurrentPosture ==
                    EnemyPosture.Crawling;
                return actor.transform.position.z > 4.5f;
            },
            "Enemy did not traverse the low passage on the crawling NavMesh.");

        Assert.That(
            observedCrawling,
            Is.True,
            "Enemy crossed a low passage without switching to crawling.");

        yield return WaitForCondition(
            () =>
            {
                navigator.TryMoveTo(destination, config.chaseSpeed);
                return postureController.CurrentPosture ==
                       EnemyPosture.Standing;
            },
            "Enemy did not return to standing posture after leaving the low passage.");

        Assert.That(agent.agentTypeID, Is.EqualTo(standingAgentTypeId));
        Assert.That(agent.isOnNavMesh, Is.True);
    }

    private IEnumerator StartHost()
    {
        GameObject root = Track(new GameObject("Enemy NavMesh host"));
        UnityTransport transport = root.AddComponent<UnityTransport>();
        manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            EnableSceneManagement = false,
            ProtocolVersion = 7
        };
        transport.SetConnectionData("127.0.0.1", 0, "127.0.0.1");

        Assert.That(manager.StartHost(), Is.True);
        yield return WaitForCondition(
            () => manager.IsHost,
            "Enemy test host did not start.");
    }

    private GameplayNoiseWorldService CreateNoiseWorld()
    {
        GameObject root = Track(new GameObject("Gameplay noise world"));
        root.SetActive(false);
        GameplayNoiseWorldService service =
            root.AddComponent<GameplayNoiseWorldService>();
        PlayModeTestReflection.SetField(service, "networkManager", manager);
        root.SetActive(true);

        Assert.That(service.Construct(manager), Is.True);
        return service;
    }

    private NetworkEnemyController CreateSpawnedProductionEnemy(
        GameObject enemyPrefab,
        GameplayNoiseWorldService noiseWorld,
        Vector3 position)
    {
        GameObject instance = Track(
            UnityEngine.Object.Instantiate(
                enemyPrefab,
                position,
                Quaternion.identity));
        instance.name = "Production baked NavMesh enemy";

        EnemyTargetDetector detector =
            instance.GetComponent<EnemyTargetDetector>();
        detector.Construct(noiseWorld);

        NetworkObject networkObject =
            instance.GetComponent<NetworkObject>();
        PlayModeTestReflection.SetField(
            networkObject,
            "NetworkManagerOwner",
            manager);
        networkObject.Spawn();

        Assert.That(networkObject.IsSpawned, Is.True);
        return instance.GetComponent<NetworkEnemyController>();
    }

    private EnemyTarget CreateSpawnedTarget(
        Vector3 position,
        bool canBeDetected)
    {
        GameObject targetObject =
            Track(new GameObject("Baked NavMesh player target"));
        targetObject.SetActive(false);
        targetObject.layer = PlayerLayer;
        targetObject.transform.position = position;

        NetworkObject networkObject =
            targetObject.AddComponent<NetworkObject>();
        CapsuleCollider collider =
            targetObject.AddComponent<CapsuleCollider>();
        collider.height = 2f;
        collider.radius = 0.35f;
        collider.center = Vector3.up;

        LogAssert.Expect(
            LogType.Error,
            new Regex("EnemyTarget has invalid visibility configuration:"));
        EnemyTarget target = targetObject.AddComponent<EnemyTarget>();
        PlayModeTestReflection.SetField(
            target,
            "visibilityColliders",
            new Collider[] { collider });
        PlayModeTestReflection.SetField(
            target,
            "canBeDetected",
            canBeDetected);
        targetObject.SetActive(true);

        PlayModeTestReflection.SetField(
            networkObject,
            "NetworkManagerOwner",
            manager);
        networkObject.Spawn();

        Assert.That(networkObject.IsSpawned, Is.True);
        return target;
    }

    private DoorInteractableObject CreateEnemyDoor(
        Vector3 position,
        out EnemyDoorInteractionZone zone)
    {
        GameObject doorObject = Track(new GameObject("Enemy route door"));
        doorObject.layer = 9;
        doorObject.transform.position = position;
        BoxCollider collider = doorObject.AddComponent<BoxCollider>();
        collider.size = new Vector3(2f, 2f, 0.2f);
        collider.center = Vector3.up;

        DoorInteractableObject door =
            doorObject.AddComponent<DoorInteractableObject>();
        zone = doorObject.AddComponent<EnemyDoorInteractionZone>();
        PlayModeTestReflection.SetField(zone, "interactionCollider", collider);
        PlayModeTestReflection.SetField(zone, "linkedDoor", door);
        Physics.SyncTransforms();
        return door;
    }

    private EnemyConfig CloneNavigationConfig(EnemyConfig source)
    {
        EnemyConfig clone = Track(UnityEngine.Object.Instantiate(source));
        EnemyMovementConfig movement =
            Track(UnityEngine.Object.Instantiate(source.MovementProfile));
        EnemyPostureConfig posture =
            Track(UnityEngine.Object.Instantiate(source.PostureProfile));

        movement.chaseSpeed = 4f;
        movement.acceleration = 30f;
        PlayModeTestReflection.SetField(
            clone,
            "movementProfile",
            movement);
        PlayModeTestReflection.SetField(
            clone,
            "postureProfile",
            posture);
        return clone;
    }

    private void BakeOpenArena(
        int standingAgentTypeId,
        int crawlingAgentTypeId)
    {
        GameObject root = Track(new GameObject("Prebuilt NavMesh arena"));
        CreateGeometry(
            "Arena floor",
            new Vector3(0f, -0.1f, 0f),
            new Vector3(16f, 0.2f, 24f),
            root.transform);
        BakeSurfaces(
            root,
            standingAgentTypeId,
            crawlingAgentTypeId);
    }

    private void BakeLowCeilingCorridor(
        int standingAgentTypeId,
        int crawlingAgentTypeId)
    {
        GameObject root =
            Track(new GameObject("Prebuilt low ceiling corridor"));
        CreateGeometry(
            "Corridor floor",
            new Vector3(0f, -0.1f, 0f),
            new Vector3(5f, 0.2f, 16f),
            root.transform);
        CreateGeometry(
            "Corridor left wall",
            new Vector3(-2.4f, 1f, 0f),
            new Vector3(0.2f, 2f, 16f),
            root.transform);
        CreateGeometry(
            "Corridor right wall",
            new Vector3(2.4f, 1f, 0f),
            new Vector3(0.2f, 2f, 16f),
            root.transform);
        CreateGeometry(
            "Low ceiling",
            new Vector3(0f, 1.3f, 0f),
            new Vector3(4.8f, 0.2f, 5f),
            root.transform);
        BakeSurfaces(
            root,
            standingAgentTypeId,
            crawlingAgentTypeId);
    }

    private void BakeSurfaces(
        GameObject root,
        int standingAgentTypeId,
        int crawlingAgentTypeId)
    {
        Physics.SyncTransforms();

        BuildSurface(root, standingAgentTypeId);
        BuildSurface(root, crawlingAgentTypeId);

        Assert.That(
            surfaces[surfaces.Count - 2].navMeshData,
            Is.Not.Null,
            "Standing NavMesh was not baked before enemy startup.");
        Assert.That(
            surfaces[surfaces.Count - 1].navMeshData,
            Is.Not.Null,
            "Crawling NavMesh was not baked before enemy startup.");
    }

    private void BuildSurface(GameObject root, int agentTypeId)
    {
        NavMeshSurface surface = root.AddComponent<NavMeshSurface>();
        surface.agentTypeID = agentTypeId;
        surface.collectObjects = CollectObjects.Children;
        surface.layerMask = 1 << EnvironmentLayer;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = true;
        surface.BuildNavMesh();
        surfaces.Add(surface);
    }

    private GameObject CreateGeometry(
        string name,
        Vector3 position,
        Vector3 scale,
        Transform parent = null)
    {
        GameObject geometry = Track(
            GameObject.CreatePrimitive(PrimitiveType.Cube));
        geometry.name = name;
        geometry.layer = EnvironmentLayer;
        geometry.transform.SetParent(parent, false);
        geometry.transform.position = position;
        geometry.transform.localScale = scale;
        return geometry;
    }

    private static bool TryPlaceAgent(
        NavMeshAgent agent,
        Vector3 position)
    {
        NavMeshQueryFilter filter = new()
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };

        return NavMesh.SamplePosition(
                   position,
                   out NavMeshHit hit,
                   2f,
                   filter) &&
               agent.Warp(hit.position) &&
               agent.isOnNavMesh;
    }

    private static void GetProductionAgentTypes(
        GameObject enemyPrefab,
        out int standingAgentTypeId,
        out int crawlingAgentTypeId)
    {
        EnemyPostureController posture =
            enemyPrefab.GetComponent<EnemyPostureController>();
        Assert.That(posture, Is.Not.Null);

        standingAgentTypeId = PlayModeTestReflection.GetField<int>(
            posture,
            "standingAgentTypeId");
        crawlingAgentTypeId = PlayModeTestReflection.GetField<int>(
            posture,
            "crawlingAgentTypeId");

        Assert.That(
            NavMesh.GetSettingsByID(standingAgentTypeId).agentTypeID,
            Is.EqualTo(standingAgentTypeId));
        Assert.That(
            NavMesh.GetSettingsByID(crawlingAgentTypeId).agentTypeID,
            Is.EqualTo(crawlingAgentTypeId));
    }

    private static T LoadRequiredAsset<T>(string path)
        where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Assert.That(asset, Is.Not.Null, $"Missing test asset '{path}'.");
        return asset;
    }

    private static IEnumerator WaitForCondition(
        Func<bool> condition,
        string failureMessage)
    {
        float timeout = Time.realtimeSinceStartup + TimeoutSeconds;

        while (!condition.Invoke() && Time.realtimeSinceStartup < timeout)
            yield return null;

        Assert.That(condition.Invoke(), Is.True, failureMessage);
    }

    private T Track<T>(T value)
        where T : UnityEngine.Object
    {
        cleanup.Add(value);
        return value;
    }
}
#endif
