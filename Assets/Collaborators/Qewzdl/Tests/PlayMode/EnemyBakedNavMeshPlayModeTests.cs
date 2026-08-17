using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
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
// Only the active map root matters to the spawner; the rest of the contract is
// here because the interface asks for it.
internal sealed class SpawnerMapSessionStub : IGameMapSessionService
{
    private readonly GameMapRoot mapRoot;

    internal SpawnerMapSessionStub(GameMapRoot mapRoot)
    {
        this.mapRoot = mapRoot;
    }

    public IGameMapCatalog Catalog => null;
    public GameMapDefinition SelectedMap => null;
    public GameMapDefinition ActiveMap => null;
    public GameMapRoot ActiveMapRoot => mapRoot;
    public bool IsReadyForMatch => true;
    public EnemyConfig SelectedEnemyConfig => null;

    public event Action MapReady
    {
        add { }
        remove { }
    }

    public bool SelectMap(int mapId)
    {
        return false;
    }

    public bool SelectDifficulty(int difficultyId)
    {
        return false;
    }

    public bool TryGetPlayerSpawn(
        ulong clientId,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = default;
        rotation = default;
        return false;
    }
}

public sealed class EnemyBakedNavMeshPlayModeTests
{
    private const string FixtureScenePath =
        "Assets/Collaborators/Qewzdl/Tests/PlayMode/Scenarios/EnemyBakedNavMeshFixture.unity";
    private const float TimeoutSeconds = 12f;
    private const int EnvironmentLayer = 7;
    private const int PlayerLayer = 3;

    private readonly List<UnityEngine.Object> cleanup = new();
    private readonly List<NavMeshSurface> surfaces = new();

    private NetworkManager manager;
    private Scene fixtureScene;
    private GameObject enemyPrefab;
    private EnemyConfig enemyConfig;

    [UnitySetUp]
    public IEnumerator SetUpFixture()
    {
        Assert.That(
            SceneManager.GetSceneByPath(FixtureScenePath).isLoaded,
            Is.False,
            "Enemy baked NavMesh fixture scene was already loaded.");

#if UNITY_EDITOR
        AsyncOperation loadOperation =
            EditorSceneManager.LoadSceneAsyncInPlayMode(
                FixtureScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
#else
        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(
            FixtureScenePath,
            LoadSceneMode.Additive);
#endif
        Assert.That(
            loadOperation,
            Is.Not.Null,
            $"Could not load test fixture scene '{FixtureScenePath}'.");
        yield return loadOperation;

        fixtureScene = SceneManager.GetSceneByPath(FixtureScenePath);
        Assert.That(fixtureScene.IsValid(), Is.True);
        Assert.That(fixtureScene.isLoaded, Is.True);

        EnemyBakedNavMeshTestFixture fixture = null;
        GameObject[] roots = fixtureScene.GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            EnemyBakedNavMeshTestFixture candidate =
                roots[i].GetComponent<EnemyBakedNavMeshTestFixture>();

            if (candidate == null)
                continue;

            Assert.That(
                fixture,
                Is.Null,
                "Fixture scene contains multiple enemy test fixtures.");
            fixture = candidate;
        }

        Assert.That(
            fixture,
            Is.Not.Null,
            "Fixture scene has no enemy test fixture.");
        enemyPrefab = fixture.EnemyPrefab;
        enemyConfig = fixture.EnemyConfig;
        Assert.That(enemyPrefab, Is.Not.Null);
        Assert.That(enemyConfig, Is.Not.Null);
    }

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

        if (fixtureScene.IsValid() && fixtureScene.isLoaded)
        {
            AsyncOperation unloadOperation =
                SceneManager.UnloadSceneAsync(fixtureScene);

            if (unloadOperation != null)
                yield return unloadOperation;
        }

        fixtureScene = default;
        enemyPrefab = null;
        enemyConfig = null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator ServerEnemy_OnPrebuiltNavMesh_HandlesNoiseVisionChaseLossAndBlockedAttack()
    {
        yield return StartHost();

        EnemyConfig config = enemyConfig;
        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        GameplayNoiseWorldService noiseWorld = CreateNoiseWorld();
        EnemyTarget target = CreateSpawnedTarget(
            new Vector3(0f, 0f, 1.2f),
            canBeDetected: true);

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

        PlayModeTestReflection.SetField(target, "canBeDetected", false);
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
        EnemyConfig config = enemyConfig;
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
        actor.transform.position = new Vector3(0f, 0f, -5f);

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
        Assert.That(
            door.IsReservedByEnemy,
            Is.False,
            "The door should initially be outside the configured detection radius.");

        yield return WaitForCondition(
            () =>
            {
                navigator.TickNavigationGate();
                return door.IsReservedByEnemy;
            },
            "Enemy did not detect the blocking door while following a previously requested path.");

        Assert.That(
            actor.transform.position.z,
            Is.LessThan(-0.1f),
            "Enemy crossed the closed door before beginning the authoritative interaction.");
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
                navigator.TickNavigationGate();
                return actor.transform.position.z > 0.5f;
            },
            "Enemy did not resume the original path after opening the door.");

        Assert.That(door.IsOpen, Is.True);
        Assert.That(door.IsReservedByEnemy, Is.True);
    }

    [UnityTest]
    public IEnumerator Navigator_OnPrebuiltNavMesh_ResumesHaltedAgentWithoutDestinationChange()
    {
        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        GameObject actor = Track(new GameObject("Halted navigation enemy"));
        actor.SetActive(false);
        actor.layer = 6;
        actor.transform.position = new Vector3(0f, 0f, -5f);

        NavMeshAgent agent = actor.AddComponent<NavMeshAgent>();
        agent.agentTypeID = standingAgentTypeId;
        agent.speed = 3f;
        agent.acceleration = 20f;
        EnemyNavigator navigator = actor.AddComponent<EnemyNavigator>();
        actor.SetActive(true);

        Assert.That(
            TryPlaceAgent(agent, actor.transform.position),
            Is.True,
            "Halted navigation enemy could not be placed on NavMesh.");

        navigator.Configure(enemyConfig);

        Vector3 destination = new(0f, 0f, 5f);
        Assert.That(navigator.TryMoveTo(destination, 3f), Is.True);

        yield return WaitForCondition(
            () => agent.hasPath && !agent.pathPending,
            "Navigator never applied the initial path.");

        // Whatever halted the agent - a door interaction is the common one -
        // leaves a perfectly valid path behind, so the repath scheduler keeps
        // deferring. The destination never moves because the player is
        // barricaded and standing still.
        agent.isStopped = true;
        float haltPosition = actor.transform.position.z;

        yield return WaitForCondition(
            () =>
            {
                navigator.TryMoveTo(destination, 3f);
                return actor.transform.position.z > haltPosition + 0.5f;
            },
            "Enemy stayed halted while re-requesting an unchanged destination.");

        Assert.That(agent.isStopped, Is.False);
    }

    [UnityTest]
    public IEnumerator Navigator_OnPrebuiltNavMesh_RoutesAroundSpawnedItem()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        NetworkItemTestDraggable item =
            CreateSpawnedNavigationItem(Vector3.zero);
        ItemNavigationObstacle itemNavigation =
            item.GetComponent<ItemNavigationObstacle>();
        NavMeshObstacle obstacle = item.GetComponent<NavMeshObstacle>();

        yield return WaitForCondition(
            () => itemNavigation.IsBlockingNavigation && obstacle.carving,
            "Spawned item did not become a server navigation obstacle.");
        yield return new WaitForSecondsRealtime(0.75f);

        GameObject actor = Track(new GameObject("Item avoidance enemy"));
        actor.SetActive(false);
        actor.layer = 6;
        actor.transform.position = new Vector3(0f, 0f, -5f);

        NavMeshAgent agent = actor.AddComponent<NavMeshAgent>();
        agent.agentTypeID = standingAgentTypeId;
        agent.radius = 0.5f;
        agent.speed = 3f;
        agent.acceleration = 20f;
        EnemyNavigator navigator = actor.AddComponent<EnemyNavigator>();
        actor.SetActive(true);

        Assert.That(
            TryPlaceAgent(agent, actor.transform.position),
            Is.True,
            "Item avoidance enemy could not be placed on NavMesh.");
        navigator.Configure(enemyConfig);

        Vector3 destination = new(0f, 0f, 5f);
        Assert.That(navigator.TryMoveTo(destination, 3f), Is.True);

        float maxLateralDistance = 0f;
        float minimumItemDistance = float.PositiveInfinity;
        float timeout = Time.realtimeSinceStartup + TimeoutSeconds;

        while (actor.transform.position.z < 2f &&
               Time.realtimeSinceStartup < timeout)
        {
            Vector3 position = actor.transform.position;
            maxLateralDistance = Mathf.Max(
                maxLateralDistance,
                Mathf.Abs(position.x));
            minimumItemDistance = Mathf.Min(
                minimumItemDistance,
                Vector2.Distance(
                    new Vector2(position.x, position.z),
                    Vector2.zero));
            yield return null;
        }

        Assert.That(
            actor.transform.position.z,
            Is.GreaterThanOrEqualTo(2f),
            "Enemy did not continue after routing around the item.");
        Assert.That(
            maxLateralDistance,
            Is.GreaterThan(1f),
            "Enemy kept a straight route through the item obstacle.");
        Assert.That(
            minimumItemDistance,
            Is.GreaterThan(1f),
            "Enemy capsule intersected the item while navigating around it.");
    }

    [UnityTest]
    public IEnumerator Navigator_OnPrebuiltNavMesh_RoutesAroundHidingPlace()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        HidingPlaceNavigationObstacle hidingNavigation =
            CreateSpawnedHidingNavigationObstacle(Vector3.zero);
        NavMeshObstacle obstacle =
            hidingNavigation.GetComponent<NavMeshObstacle>();

        yield return WaitForCondition(
            () => hidingNavigation.IsBlockingNavigation,
            "Spawned hiding place did not carve the server NavMesh.");
        yield return new WaitForSecondsRealtime(0.5f);

        GameObject actor = Track(new GameObject("Hiding avoidance enemy"));
        actor.SetActive(false);
        actor.layer = 6;
        actor.transform.position = new Vector3(0f, 0f, -5f);

        NavMeshAgent agent = actor.AddComponent<NavMeshAgent>();
        agent.agentTypeID = standingAgentTypeId;
        agent.radius = 0.5f;
        agent.speed = 3f;
        agent.acceleration = 20f;
        EnemyNavigator navigator = actor.AddComponent<EnemyNavigator>();
        actor.SetActive(true);

        Assert.That(
            TryPlaceAgent(agent, actor.transform.position),
            Is.True,
            "Hiding avoidance enemy could not be placed on NavMesh.");
        navigator.Configure(enemyConfig);

        Vector3 destination = new(0f, 0f, 5f);
        Assert.That(navigator.TryMoveTo(destination, 3f), Is.True);

        float maxLateralDistance = 0f;
        float minimumHidingPlaceDistance = float.PositiveInfinity;
        float timeout = Time.realtimeSinceStartup + TimeoutSeconds;

        while (actor.transform.position.z < 2f &&
               Time.realtimeSinceStartup < timeout)
        {
            Vector3 position = actor.transform.position;
            maxLateralDistance = Mathf.Max(
                maxLateralDistance,
                Mathf.Abs(position.x));
            minimumHidingPlaceDistance = Mathf.Min(
                minimumHidingPlaceDistance,
                Vector2.Distance(
                    new Vector2(position.x, position.z),
                    Vector2.zero));
            yield return null;
        }

        Assert.That(obstacle.enabled, Is.True);
        Assert.That(obstacle.carving, Is.True);
        Assert.That(
            actor.transform.position.z,
            Is.GreaterThanOrEqualTo(2f),
            "Enemy did not continue after routing around the hiding place.");
        Assert.That(
            maxLateralDistance,
            Is.GreaterThan(1f),
            "Enemy kept a straight route through the hiding place.");
        Assert.That(
            minimumHidingPlaceDistance,
            Is.GreaterThan(1f),
            "Enemy capsule intersected the hiding place while navigating.");
    }

    // Asserts the observable thing - the enemy actually comes to a stop - not
    // that a timer counted down. Without a dwell it never stops once between
    // arriving and giving up, so no stationary window exists at all.
    [UnityTest]
    public IEnumerator InvestigateState_StandsStillAtReachedPoints()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        float dwellDuration = enemyConfig.investigationPointDwellDuration;

        Assert.That(
            dwellDuration,
            Is.GreaterThan(0f),
            "The shipped config disables dwelling, so this test would pass " +
            "vacuously.");
        Assert.That(
            enemyConfig.investigationLookAroundAngle,
            Is.GreaterThan(0f),
            "The shipped config disables looking around, so the yaw half of " +
            "this test would pass vacuously.");

        EnemyInvestigateState state = CreateInvestigateState(
            new Vector3(0f, 0f, -6f),
            standingAgentTypeId,
            out GameObject actor,
            out EnemyBlackboard blackboard,
            out List<EnemyState> stateChanges);

        blackboard.InvestigationMemory.RememberLastKnownTargetPosition(
            Vector3.zero);

        state.Enter();

        // Speed, not per-frame distance: batchmode runs unrendered and fast, so
        // a moving enemy covers well under a centimetre per frame and any
        // distance threshold would call that standing still.
        const float stationarySpeed = 0.2f;
        float lookAroundAngle = enemyConfig.investigationLookAroundAngle;
        float longestStillWindow = 0f;
        float currentStillWindow = 0f;
        float widestYawSweep = 0f;
        float windowYawMin = 0f;
        float windowYawMax = 0f;
        float windowStartYaw = actor.transform.eulerAngles.y;
        Vector3 previousPosition = actor.transform.position;
        float timeout = Time.realtimeSinceStartup + TimeoutSeconds;

        while (stateChanges.Count == 0 &&
               Time.realtimeSinceStartup < timeout &&
               (longestStillWindow < dwellDuration * 0.5f ||
                widestYawSweep < lookAroundAngle))
        {
            state.Tick(Time.deltaTime);

            yield return null;

            Vector3 currentPosition = actor.transform.position;
            float speed = Time.deltaTime > 0f
                ? (currentPosition - previousPosition).magnitude /
                  Time.deltaTime
                : 0f;

            if (speed < stationarySpeed)
            {
                if (currentStillWindow <= 0f)
                {
                    windowStartYaw = actor.transform.eulerAngles.y;
                    windowYawMin = 0f;
                    windowYawMax = 0f;
                }

                currentStillWindow += Time.deltaTime;

                // Relative to the yaw this window started at, so the
                // measurement never trips over the 0/360 wrap.
                float yawOffset = Mathf.DeltaAngle(
                    windowStartYaw,
                    actor.transform.eulerAngles.y);

                windowYawMin = Mathf.Min(windowYawMin, yawOffset);
                windowYawMax = Mathf.Max(windowYawMax, yawOffset);
                widestYawSweep = Mathf.Max(
                    widestYawSweep,
                    windowYawMax - windowYawMin);
            }
            else
            {
                currentStillWindow = 0f;
            }

            longestStillWindow = Mathf.Max(
                longestStillWindow,
                currentStillWindow);
            previousPosition = currentPosition;
        }

        Assert.That(
            longestStillWindow,
            Is.GreaterThanOrEqualTo(dwellDuration * 0.5f),
            "Enemy never stood still at a point long enough to look around.");

        // Turns one way then the other, so a completed look spans about twice
        // the configured angle. Asserting one angle leaves room for a turn that
        // ran out of dwell before finishing its second half.
        Assert.That(
            widestYawSweep,
            Is.GreaterThanOrEqualTo(lookAroundAngle),
            "Enemy stood still but never turned to look around.");
    }

    // A noise that lands mid-investigation updates the memory, but the brain
    // will not restart a state it is already in, so the route stays planned
    // around wherever the enemy was already looking.
    [UnityTest]
    public IEnumerator InvestigateState_StimulusFarFromTheRoute_RebuildsIt()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        EnemyInvestigateState state = CreateInvestigateState(
            new Vector3(0f, 0f, -6f),
            standingAgentTypeId,
            out GameObject _,
            out EnemyBlackboard blackboard,
            out List<EnemyState> _);

        Vector3 firstStimulus = Vector3.zero;
        blackboard.InvestigationMemory.RememberLastKnownTargetPosition(
            firstStimulus);

        state.Enter();

        yield return TickInvestigationUntil(
            state,
            () => blackboard.HasCurrentDestination &&
                  Vector3.Distance(
                      blackboard.CurrentDestination,
                      firstStimulus) < 0.5f,
            "Enemy never headed for the first stimulus.");

        // Something loud, well outside the ring being walked.
        Vector3 secondStimulus = new(0f, 0f, 8f);
        blackboard.InvestigationMemory.RememberLastKnownTargetPosition(
            secondStimulus);

        yield return TickInvestigationUntil(
            state,
            () => blackboard.HasCurrentDestination &&
                  Vector3.Distance(
                      blackboard.CurrentDestination,
                      secondStimulus) < 0.5f,
            "Enemy kept walking the old route after a new stimulus landed " +
            "well outside it.");
    }

    // The other half of the same rule: a stimulus inside the ring must not
    // rebuild anything, or every repeat of one noise restarts the route.
    [UnityTest]
    public IEnumerator InvestigateState_StimulusInsideTheRoute_KeepsIt()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        EnemyInvestigateState state = CreateInvestigateState(
            new Vector3(0f, 0f, -6f),
            standingAgentTypeId,
            out GameObject _,
            out EnemyBlackboard blackboard,
            out List<EnemyState> _);

        Vector3 firstStimulus = Vector3.zero;
        blackboard.InvestigationMemory.RememberLastKnownTargetPosition(
            firstStimulus);

        state.Enter();

        yield return TickInvestigationUntil(
            state,
            () => blackboard.HasCurrentDestination &&
                  Vector3.Distance(
                      blackboard.CurrentDestination,
                      firstStimulus) < 0.5f,
            "Enemy never headed for the first stimulus.");

        Vector3 nearbyStimulus = new(0f, 0f, 1.5f);
        blackboard.InvestigationMemory.RememberLastKnownTargetPosition(
            nearbyStimulus);

        float endTime = Time.realtimeSinceStartup + 1f;

        while (Time.realtimeSinceStartup < endTime)
        {
            state.Tick(Time.deltaTime);

            Assert.That(
                Vector3.Distance(
                    blackboard.CurrentDestination,
                    nearbyStimulus),
                Is.GreaterThan(0.5f),
                "A stimulus inside the ring restarted the route.");

            yield return null;
        }
    }

    // The original complaint: an enemy busy with something else walks straight
    // past an occupied box and yanks the player out of it. An enemy that never
    // watched anyone climb in holds no reference, and without a reference the
    // box is just scenery no matter how close the route passes.
    [UnityTest]
    public IEnumerator InvestigateState_WithoutAReference_WalksPastOccupiedBox()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        HidingPlaceInteractable hidingPlace =
            CreateSpawnedHidingPlace(Vector3.zero);
        PlayerHidingController occupant =
            CreateSpawnedHidingPlayer(new Vector3(0.8f, 0f, -1.2f));
        yield return OccupyHidingPlace(hidingPlace, occupant);

        EnemyInvestigateState state = CreateInvestigateState(
            new Vector3(0f, 0f, -6f),
            standingAgentTypeId,
            out GameObject actor,
            out EnemyBlackboard blackboard,
            out List<EnemyState> stateChanges);

        // A noise on the far side of the box, and nothing else. The route to it
        // runs right past the occupant.
        blackboard.InvestigationMemory.RememberLastKnownTargetPosition(
            new Vector3(0f, 0f, 9f));

        state.Enter();

        float timeout = Time.realtimeSinceStartup + TimeoutSeconds;
        bool walkedPastTheHidingPlace = false;
        bool reachedTheRealStimulus = false;

        while (stateChanges.Count == 0 &&
               Time.realtimeSinceStartup < timeout)
        {
            state.Tick(Time.deltaTime);

            Vector3 position = actor.transform.position;
            walkedPastTheHidingPlace |= position.z > 1f;
            reachedTheRealStimulus |= position.z > 8f;

            Assert.That(
                hidingPlace.State,
                Is.EqualTo(HidingTransitionState.Occupied),
                "Enemy opened an occupied box it had no reason to suspect.");

            if (reachedTheRealStimulus)
                break;

            yield return null;
        }

        Assert.That(
            walkedPastTheHidingPlace,
            Is.True,
            "Enemy never walked past the hiding place, so nothing was covered.");
        Assert.That(
            reachedTheRealStimulus,
            Is.True,
            "Enemy did not investigate the position the stimulus actually came from.");
        Assert.That(occupant.IsHidden, Is.True);
    }

    // The closest thing to a playtest that runs unattended: the shipped enemy
    // prefab with its real brain, a real networked player and a real hiding
    // place on a baked NavMesh. Every other test here drives one link of the
    // chain in isolation - perception with a pinned state, or the investigate
    // state with memory set by hand. This one is the whole chain, which is
    // where the interesting breakages live.
    [UnityTest]
    public IEnumerator ProductionEnemy_WatchingPlayerHide_ComesOverAndOpensTheBox()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        GameplayNoiseWorldService noiseWorld = CreateNoiseWorld();
        HidingPlaceInteractable hidingPlace =
            CreateSpawnedHidingPlace(Vector3.zero);
        PlayerHidingController occupant =
            CreateSpawnedHidingPlayer(new Vector3(0.8f, 0f, -1.2f));

        NetworkEnemyController enemy = CreateSpawnedProductionEnemy(
            enemyPrefab,
            noiseWorld,
            new Vector3(0f, 0f, -8f));
        EnemyServerRuntime runtime =
            enemy.GetComponent<EnemyServerRuntime>();
        NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();

        yield return WaitForCondition(
            () => runtime.IsRunning && agent.enabled && agent.isOnNavMesh,
            "Production enemy did not start on the baked NavMesh.");

        yield return WaitForCondition(
            () => enemy.HasTarget &&
                  (enemy.CurrentState == EnemyState.Chase ||
                   enemy.CurrentState == EnemyState.Attack),
            "Production enemy never saw the player standing in the open.");

        // Climb in while it is actively looking at us.
        Assert.That(hidingPlace.TryRequestEnter(occupant), Is.True);
        Assert.That(
            hidingPlace.State,
            Is.EqualTo(HidingTransitionState.Occupied),
            "Entry is instant, so the enemy never sees an Entering state.");

        // Visual memory keeps it chasing the live position for a couple of
        // seconds before the investigation even starts, so allow real time.
        EnemyBlackboard enemyBlackboard =
            PlayModeTestReflection.GetField<EnemyBlackboard>(
                runtime,
                "blackboard");
        List<EnemyState> observedStates = new();
        bool everRememberedHidingPlace = false;
        bool sampledInvestigateEntry = false;
        Vector3 investigateLastKnown = Vector3.zero;
        float closestApproach = float.PositiveInfinity;
        float openTimeout = Time.realtimeSinceStartup + 25f;

        while (hidingPlace.State == HidingTransitionState.Occupied &&
               Time.realtimeSinceStartup < openTimeout)
        {
            if (observedStates.Count == 0 ||
                observedStates[observedStates.Count - 1] != enemy.CurrentState)
            {
                observedStates.Add(enemy.CurrentState);
            }

            everRememberedHidingPlace |=
                enemyBlackboard.InvestigationMemory.ObservedHidingPlace != null;

            if (!sampledInvestigateEntry &&
                enemy.CurrentState == EnemyState.Investigate)
            {
                sampledInvestigateEntry = true;
                investigateLastKnown = enemyBlackboard
                    .InvestigationMemory.LastKnownTargetPosition;
            }

            closestApproach = Mathf.Min(
                closestApproach,
                Vector3.Distance(
                    enemy.transform.position,
                    hidingPlace.EnemyInvestigationPosition));

            yield return null;
        }

        Assert.That(
            hidingPlace.State != HidingTransitionState.Occupied,
            Is.True,
            "Production enemy watched the player climb in and never came to " +
            "open the box. " +
            $"states=[{string.Join(",", observedStates)}] " +
            $"everRememberedHidingPlace={everRememberedHidingPlace} " +
            $"investigateLastKnown={investigateLastKnown} " +
            $"closestApproach={closestApproach:F2} " +
            $"anchor={hidingPlace.EnemyInvestigationPosition} " +
            $"reachDistance={enemy.Config.investigationReachDistance:F2} " +
            $"openDistance={hidingPlace.Configuration.EnemyInvestigationDistance:F2}");

        yield return WaitForCondition(
            () => !occupant.IsInHidingSequence,
            "Opened hiding place did not release its occupant.");
    }

    // The entry completes inside a single call, so no polling sensor can ever
    // catch the player mid-climb. What an enemy CAN observe is the transition:
    // the target it was pursuing stopped being detectable, and it is now inside
    // a hiding place. That is the signal this whole feature rests on.
    [UnityTest]
    public IEnumerator EnemyWatchingPlayerVanish_LearnsTheHidingPlace()
    {
        yield return StartHost();

        GameplayNoiseWorldService noiseWorld = CreateNoiseWorld();
        HidingPlaceInteractable hidingPlace =
            CreateSpawnedHidingPlace(Vector3.zero);
        PlayerHidingController occupant =
            CreateSpawnedHidingPlayer(new Vector3(0.8f, 0f, -1.2f));
        EnemyTargetDetector detector =
            CreateWatchingDetector(new Vector3(0f, 0f, -8f), noiseWorld);

        EnemyBlackboard blackboard = new();
        EnemyPerceptionRuntime perception = new(
            enemyConfig,
            detector,
            usesTargetDetection: true,
            blackboard,
            _ => { });

        yield return TickPerceptionUntil(
            perception,
            EnemyState.Chase,
            () => blackboard.TargetMemory.HasTarget,
            "Enemy never confirmed the player standing in the open.");

        Assert.That(
            blackboard.InvestigationMemory.ObservedHidingPlace,
            Is.Null,
            "Enemy knew about a hiding place before the player used one.");

        Assert.That(hidingPlace.TryRequestEnter(occupant), Is.True);
        Assert.That(
            hidingPlace.State,
            Is.EqualTo(HidingTransitionState.Occupied),
            "This test is only meaningful while the entry is instant.");

        yield return TickPerceptionUntil(
            perception,
            EnemyState.Chase,
            () => blackboard.InvestigationMemory.ObservedHidingPlace != null,
            "Enemy watched the player vanish but never worked out which " +
            "hiding place they used.");

        Assert.That(
            blackboard.InvestigationMemory.ObservedHidingPlace,
            Is.SameAs(hidingPlace));
    }

    // The flip side, and the reason the signal is tied to losing a pursued
    // target rather than to the hiding place itself: an enemy that never had
    // eyes on the player must not be handed its location.
    [UnityTest]
    public IEnumerator EnemyThatNeverSawThePlayer_LearnsNothing()
    {
        yield return StartHost();

        GameplayNoiseWorldService noiseWorld = CreateNoiseWorld();
        HidingPlaceInteractable hidingPlace =
            CreateSpawnedHidingPlace(Vector3.zero);
        PlayerHidingController occupant =
            CreateSpawnedHidingPlayer(new Vector3(0.8f, 0f, -1.2f));

        yield return OccupyHidingPlace(hidingPlace, occupant);

        EnemyTargetDetector detector =
            CreateWatchingDetector(new Vector3(0f, 0f, -8f), noiseWorld);
        EnemyBlackboard blackboard = new();
        EnemyPerceptionRuntime perception = new(
            enemyConfig,
            detector,
            usesTargetDetection: true,
            blackboard,
            _ => { });

        float endTime = Time.realtimeSinceStartup + 2f;

        while (Time.realtimeSinceStartup < endTime)
        {
            perception.Tick(Time.deltaTime, EnemyState.Chase);

            Assert.That(
                blackboard.InvestigationMemory.ObservedHidingPlace == null,
                Is.True,
                "An enemy that never saw the player still learned which " +
                "hiding place they were in.");

            yield return null;
        }

        Assert.That(blackboard.TargetMemory.HasTarget, Is.False);
    }

    // Visual memory hands out the target's live position, so by the time the
    // grace period lapses the last known position sits INSIDE the box - and
    // inside the hole the box carves out of the NavMesh. Investigating that
    // point is unreachable; the enemy used to give up and forget a player it
    // had just watched hide.
    [UnityTest]
    public IEnumerator InvestigateState_LastKnownPositionInsideTheBox_StillOpensIt()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        HidingPlaceInteractable hidingPlace =
            CreateSpawnedHidingPlace(Vector3.zero);
        PlayerHidingController occupant =
            CreateSpawnedHidingPlayer(new Vector3(0.8f, 0f, -1.2f));
        yield return OccupyHidingPlace(hidingPlace, occupant);

        EnemyInvestigateState state = CreateInvestigateState(
            new Vector3(0f, 0f, -6f),
            standingAgentTypeId,
            out GameObject _,
            out EnemyBlackboard blackboard,
            out List<EnemyState> _);

        blackboard.InvestigationMemory.RememberObservedHidingPlace(hidingPlace);
        blackboard.InvestigationMemory.RememberLastKnownTargetPosition(
            occupant.transform.position);

        state.Enter();

        yield return TickInvestigationUntil(
            state,
            () => hidingPlace.State != HidingTransitionState.Occupied,
            "Enemy gave up instead of checking the hiding place it watched " +
            "the player climb into.");

        yield return WaitForCondition(
            () => !occupant.IsInHidingSequence &&
                  hidingPlace.State == HidingTransitionState.Available,
            "Opened hiding place did not release its occupant.");
    }

    [UnityTest]
    public IEnumerator InvestigateState_StimulusAtHidingPlace_StillOpensIt()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        HidingPlaceInteractable hidingPlace =
            CreateSpawnedHidingPlace(Vector3.zero);
        PlayerHidingController occupant =
            CreateSpawnedHidingPlayer(new Vector3(0.8f, 0f, -1.2f));
        yield return OccupyHidingPlace(hidingPlace, occupant);

        EnemyInvestigateState state = CreateInvestigateState(
            new Vector3(0f, 0f, -6f),
            standingAgentTypeId,
            out GameObject _,
            out EnemyBlackboard blackboard,
            out List<EnemyState> _);

        // What seeing the target climb in, or hearing the entry noise, leaves
        // behind: a last known position right next to the hiding place.
        blackboard.InvestigationMemory.RememberObservedHidingPlace(hidingPlace);
        blackboard.InvestigationMemory.RememberLastKnownTargetPosition(
            new Vector3(0f, 0f, -1.2f));

        state.Enter();

        yield return TickInvestigationUntil(
            state,
            () => hidingPlace.State != HidingTransitionState.Occupied,
            "Enemy did not open the hiding place its investigation was about.");

        yield return WaitForCondition(
            () => !occupant.IsInHidingSequence &&
                  hidingPlace.State == HidingTransitionState.Available,
            "Opened hiding place did not release its occupant.");
    }

    [UnityTest]
    public IEnumerator Navigator_OnPrebuiltNavMesh_RoutesAroundItemUsingPhysicsMotor()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        NetworkItemTestDraggable item = CreateSpawnedNavigationItem(
            Vector3.zero,
            makeDynamic: true,
            useGravity: true);
        Rigidbody itemBody = item.GetComponent<Rigidbody>();
        ItemNavigationObstacle itemNavigation =
            item.GetComponent<ItemNavigationObstacle>();

        yield return WaitForCondition(
            () =>
                itemNavigation.IsBlockingNavigation &&
                item.GetComponent<NavMeshObstacle>().carving,
            "Grounded item did not carve an alternative route.");

        Assert.That(
            itemBody.position.y,
            Is.EqualTo(0f).Within(0.05f),
            "The regression fixture must begin with the item resting on the floor.");

        GameObject actor = Track(new GameObject("Item avoidance enemy"));
        actor.SetActive(false);
        actor.layer = 6;
        actor.transform.position = new Vector3(0f, 0f, -4f);
        actor.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

        NetworkObject networkObject = actor.AddComponent<NetworkObject>();
        CapsuleCollider bodyCollider = actor.AddComponent<CapsuleCollider>();
        bodyCollider.radius = 0.5f;
        bodyCollider.height = 2f;
        bodyCollider.center = Vector3.up;

        NavMeshAgent agent = actor.AddComponent<NavMeshAgent>();
        agent.agentTypeID = standingAgentTypeId;
        agent.radius = 0.5f;
        agent.speed = 3f;
        agent.acceleration = 20f;

        Rigidbody enemyBody = actor.AddComponent<Rigidbody>();
        EnemyPhysicsMotor physicsMotor =
            actor.AddComponent<EnemyPhysicsMotor>();
        EnemyNavigator navigator = actor.AddComponent<EnemyNavigator>();
        EnemyItemPusher pusher = actor.AddComponent<EnemyItemPusher>();
        actor.SetActive(true);

        PlayModeTestReflection.SetField(
            networkObject,
            "NetworkManagerOwner",
            manager);
        networkObject.Spawn();

        Assert.That(networkObject.IsSpawned, Is.True);
        Assert.That(
            TryPlaceAgent(agent, actor.transform.position),
            Is.True,
            "Item pushing enemy could not be placed on NavMesh.");

        navigator.Configure(enemyConfig);

        Vector3 destination = new(0f, 0f, 5f);
        Assert.That(navigator.TryMoveTo(destination, 3f), Is.True);

        float settlingObservationEnd =
            Time.realtimeSinceStartup + 0.2f;

        while (Time.realtimeSinceStartup < settlingObservationEnd)
        {
            Assert.That(navigator.TryMoveTo(destination, 3f), Is.True);
            yield return null;
        }

        float maxLateralDistance = 0f;
        float timeout = Time.realtimeSinceStartup + TimeoutSeconds;

        while (actor.transform.position.z < 2f &&
               Time.realtimeSinceStartup < timeout)
        {
            maxLateralDistance = Mathf.Max(
                maxLateralDistance,
                Mathf.Abs(actor.transform.position.x));
            yield return null;
        }

        Assert.That(pusher.enabled, Is.True);
        Assert.That(physicsMotor.IsDrivingServerBody, Is.True);
        Assert.That(enemyBody.isKinematic, Is.False);
        Assert.That(itemBody.isKinematic, Is.False);
        Assert.That(
            actor.transform.position.z,
            Is.GreaterThanOrEqualTo(2f),
            "Enemy did not continue along the alternative route.");
        Assert.That(
            maxLateralDistance,
            Is.GreaterThan(1f),
            "Enemy did not route around the item.");
        Assert.That(
            new Vector2(itemBody.position.x, itemBody.position.z).magnitude,
            Is.LessThan(0.1f),
            "Enemy moved the item while routing around it.");
        Assert.That(
            item.GetComponent<NavMeshObstacle>().carving,
            Is.True,
            "The item stopped carving the alternative route.");
        Assert.That(
            Vector3.Dot(actor.transform.forward, agent.desiredVelocity.normalized),
            Is.GreaterThan(0.8f),
            "Physics-driven enemy did not rotate toward its movement direction.");
        Assert.That(
            item.OwnerClientId,
            Is.EqualTo(NetworkManager.ServerClientId),
            "Routing around the item unexpectedly changed its ownership.");
    }

    [UnityTest]
    public IEnumerator Navigator_OnPrebuiltNavMesh_IgnoresDraggedItemOverRoute()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakePushCorridor(standingAgentTypeId, crawlingAgentTypeId);

        NetworkItemTestDraggable item = CreateSpawnedNavigationItem(
            new Vector3(0f, 1f, 0f),
            makeDynamic: true,
            colliderSize: new Vector3(3.4f, 1.5f, 1f));
        ItemNavigationObstacle itemNavigation =
            item.GetComponent<ItemNavigationObstacle>();
        NavMeshObstacle obstacle = item.GetComponent<NavMeshObstacle>();
        NetworkVariable<bool> draggingState =
            PlayModeTestReflection.GetField<NetworkVariable<bool>>(
                item,
                "netIsDragging");

        draggingState.Value = true;

        yield return WaitForCondition(
            () =>
                item.IsBeingDragged &&
                !itemNavigation.IsBlockingNavigation &&
                !obstacle.enabled &&
                !obstacle.carving,
            "Dragged item remained an authoritative navigation obstacle.");
        yield return new WaitForSecondsRealtime(0.75f);

        GameObject actor = Track(new GameObject("Dragged-item route enemy"));
        actor.SetActive(false);
        actor.layer = 6;
        actor.transform.position = new Vector3(0f, 0f, -4f);

        NetworkObject networkObject = actor.AddComponent<NetworkObject>();
        CapsuleCollider bodyCollider = actor.AddComponent<CapsuleCollider>();
        bodyCollider.radius = 0.5f;
        bodyCollider.height = 2f;
        bodyCollider.center = Vector3.up;

        NavMeshAgent agent = actor.AddComponent<NavMeshAgent>();
        agent.agentTypeID = standingAgentTypeId;
        agent.radius = 0.5f;
        agent.speed = 3f;
        agent.acceleration = 20f;

        actor.AddComponent<EnemyItemPusher>();
        EnemyNavigator navigator = actor.AddComponent<EnemyNavigator>();
        actor.SetActive(true);

        PlayModeTestReflection.SetField(
            networkObject,
            "NetworkManagerOwner",
            manager);
        networkObject.Spawn();

        Assert.That(
            TryPlaceAgent(agent, actor.transform.position),
            Is.True,
            "Dragged-item route enemy could not be placed on NavMesh.");

        navigator.Configure(enemyConfig);
        Vector3 destination = new(0f, 0f, 4f);

        Assert.That(
            navigator.TryMoveTo(destination, 3f),
            Is.True,
            "Navigator rejected a route under a dragged item.");

        yield return WaitForCondition(
            () =>
            {
                navigator.TickNavigationGate();
                navigator.TryMoveTo(destination, 3f);
                return actor.transform.position.z > 2f;
            },
            "Enemy stopped because a dragged item hovered over its route.");
    }

    [UnityTest]
    public IEnumerator Navigator_OnPrebuiltNavMesh_RoutesThroughBarricadeOffTheSightLine()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeBarricadedRoom(standingAgentTypeId, crawlingAgentTypeId);

        NetworkItemTestDraggable firstCrate =
            CreateSpawnedNavigationItem(new Vector3(2f, 0f, 0f));
        NetworkItemTestDraggable secondCrate =
            CreateSpawnedNavigationItem(new Vector3(4f, 0f, 0f));
        ItemNavigationObstacle firstNavigation =
            firstCrate.GetComponent<ItemNavigationObstacle>();
        ItemNavigationObstacle secondNavigation =
            secondCrate.GetComponent<ItemNavigationObstacle>();

        yield return WaitForCondition(
            () =>
                firstNavigation.IsBlockingNavigation &&
                secondNavigation.IsBlockingNavigation,
            "Crates did not barricade the only doorway.");
        yield return new WaitForSecondsRealtime(0.5f);

        GameObject actor = Track(new GameObject("Barricaded room enemy"));
        actor.SetActive(false);
        actor.layer = 6;
        actor.transform.position = new Vector3(-3f, 0f, -4f);

        NetworkObject networkObject = actor.AddComponent<NetworkObject>();
        CapsuleCollider bodyCollider = actor.AddComponent<CapsuleCollider>();
        bodyCollider.radius = 0.5f;
        bodyCollider.height = 2f;
        bodyCollider.center = Vector3.up;

        NavMeshAgent agent = actor.AddComponent<NavMeshAgent>();
        agent.agentTypeID = standingAgentTypeId;
        agent.radius = 0.5f;
        agent.speed = 4f;
        agent.acceleration = 30f;

        actor.AddComponent<EnemyItemPusher>();
        EnemyNavigator navigator = actor.AddComponent<EnemyNavigator>();
        actor.SetActive(true);

        PlayModeTestReflection.SetField(
            networkObject,
            "NetworkManagerOwner",
            manager);
        networkObject.Spawn();

        Assert.That(
            TryPlaceAgent(agent, actor.transform.position),
            Is.True,
            "Barricaded room enemy could not be placed on NavMesh.");

        navigator.Configure(enemyConfig);

        // The player barricades the doorway, then is spotted over a low wall:
        // the straight line to them never touches the crates.
        Vector3 destination = new(-3f, 0f, 4f);
        NavMeshPath blockedPath = new();

        Assert.That(
            agent.CalculatePath(destination, blockedPath) &&
            blockedPath.status == NavMeshPathStatus.PathComplete,
            Is.False,
            "Barricade fixture must seal the room before the enemy repaths.");

        yield return WaitForCondition(
            () =>
            {
                navigator.TryMoveTo(destination, 4f, allowPushThrough: true);
                return actor.transform.position.z > 1f;
            },
            "Enemy stood still instead of pathing through the barricade.");

        Assert.That(
            firstNavigation.IsBlockingNavigation,
            Is.False,
            "First crate kept carving the barricaded doorway.");
        Assert.That(
            secondNavigation.IsBlockingNavigation,
            Is.False,
            "Second crate kept carving the barricaded doorway.");
    }

    [UnityTest]
    public IEnumerator PatrolPlanner_OnPrebuiltNavMesh_BuildsSafeBoundedVariation()
    {
        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        EnemyConfig config = Track(UnityEngine.Object.Instantiate(enemyConfig));
        EnemyPatrolConfig patrolProfile = Track(
            UnityEngine.Object.Instantiate(enemyConfig.PatrolProfile));
        patrolProfile.patrolRouteVariation = 2f;
        patrolProfile.patrolEdgeClearance = 1f;
        patrolProfile.patrolMaxDetourRatio = 1.5f;
        patrolProfile.patrolIntermediatePointSpacing = 4f;
        patrolProfile.patrolRouteSampleAttempts = 32;
        PlayModeTestReflection.SetField(config, "patrolProfile", patrolProfile);

        NavMeshQueryFilter filter = new()
        {
            agentTypeID = standingAgentTypeId,
            areaMask = NavMesh.AllAreas
        };

        Assert.That(
            NavMesh.SamplePosition(
                new Vector3(-6f, 0f, 0f),
                out NavMeshHit startHit,
                1f,
                filter),
            Is.True);
        Assert.That(
            NavMesh.SamplePosition(
                new Vector3(6f, 0f, 0f),
                out NavMeshHit destinationHit,
                1f,
                filter),
            Is.True);

        EnemyPatrolPathPlanner planner = new(12345);
        List<Vector3> plan = new();

        Assert.That(
            planner.TryBuildPlan(
                startHit.position,
                destinationHit.position,
                filter,
                config,
                plan),
            Is.True);
        Assert.That(
            plan.Count,
            Is.GreaterThan(1),
            "A long open patrol leg should contain safe intermediate points.");
        Assert.That(
            plan.Exists(point => Mathf.Abs(point.z) > 0.2f),
            Is.True,
            "The patrol plan should vary from the direct shortest line.");

        for (int i = 0; i < plan.Count - 1; i++)
        {
            Assert.That(
                NavMesh.FindClosestEdge(plan[i], out NavMeshHit edgeHit, filter),
                Is.True);
            Assert.That(
                edgeHit.distance,
                Is.GreaterThanOrEqualTo(patrolProfile.patrolEdgeClearance - 0.05f),
                $"Intermediate patrol point {i} is too close to the NavMesh edge.");
        }

        float directLength = CalculatePathLength(
            startHit.position,
            destinationHit.position,
            filter);
        float planLength = CalculatePlanLength(
            startHit.position,
            plan,
            filter);

        Assert.That(
            planLength,
            Is.LessThanOrEqualTo(
                directLength * patrolProfile.patrolMaxDetourRatio + 0.05f));

        patrolProfile.patrolEdgeClearance = 100f;
        EnemyPatrolPathPlanner fallbackPlanner = new(12345);

        Assert.That(
            fallbackPlanner.TryBuildPlan(
                startHit.position,
                destinationHit.position,
                filter,
                config,
                plan),
            Is.True);
        Assert.That(
            plan.Count,
            Is.EqualTo(1),
            "An impossible clearance must fall back to the valid direct route.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator PatrolController_OnPrebuiltNavMesh_FollowsCompletePlannedLeg()
    {
        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        EnemyConfig config = CloneNavigationConfig(enemyConfig);
        EnemyMovementConfig movementProfile = config.MovementProfile;
        movementProfile.patrolSpeed = 4f;

        EnemyPatrolConfig patrolProfile = Track(
            UnityEngine.Object.Instantiate(enemyConfig.PatrolProfile));
        patrolProfile.patrolRouteVariation = 2f;
        patrolProfile.patrolEdgeClearance = 1f;
        patrolProfile.patrolMaxDetourRatio = 1.5f;
        patrolProfile.patrolIntermediatePointSpacing = 4f;
        patrolProfile.patrolRouteSampleAttempts = 32;
        PlayModeTestReflection.SetField(config, "patrolProfile", patrolProfile);

        GameObject routeObject = Track(new GameObject("Planned patrol route"));
        GameObject routePoint = new("Planned patrol destination");
        routePoint.transform.SetParent(routeObject.transform);
        routePoint.transform.position = new Vector3(6f, 0f, 0f);

        EnemyPatrolRoute route = routeObject.AddComponent<EnemyPatrolRoute>();
        PlayModeTestReflection.SetField(
            route,
            "points",
            new[] { routePoint.transform });

        GameObject actor = Track(new GameObject("Planned patrol enemy"));
        actor.SetActive(false);
        actor.transform.position = new Vector3(-6f, 0f, 0f);

        NavMeshAgent agent = actor.AddComponent<NavMeshAgent>();
        agent.agentTypeID = standingAgentTypeId;
        EnemyNavigator navigator = actor.AddComponent<EnemyNavigator>();
        actor.SetActive(true);

        Assert.That(
            TryPlaceAgent(agent, actor.transform.position),
            Is.True,
            "Patrol test actor could not be placed on the baked surface.");
        navigator.Configure(config);

        EnemyBlackboard blackboard = new();
        EnemyPatrolController controller = new(
            route,
            navigator,
            config,
            blackboard);

        Assert.That(controller.MoveToNextRoutePoint(), Is.True);
        Assert.That(blackboard.HasCurrentDestination, Is.True);
        Assert.That(
            Vector3.Distance(
                blackboard.CurrentDestination,
                routePoint.transform.position),
            Is.GreaterThan(1f),
            "The controller skipped the generated intermediate patrol route.");

        bool reachedRoutePoint = false;

        // Building the route spent this enemy's share of the frame's path
        // queries, so the move that follows it is deferred and retried by the
        // navigation gate - which the server ticks every frame and this test
        // stands in for. Without the tick the agent holds a destination it
        // has no path to and never leaves its first point.
        yield return null;

        yield return WaitForCondition(
            () =>
            {
                navigator.TickNavigationGate();
                reachedRoutePoint = controller.HasReachedCurrentRoutePoint();
                return reachedRoutePoint;
            },
            "Patrol controller did not advance through the complete planned leg.");

        Assert.That(reachedRoutePoint, Is.True);
        Assert.That(
            Vector3.Distance(actor.transform.position, routePoint.transform.position),
            Is.LessThanOrEqualTo(config.patrolPointReachDistance + 0.1f));
    }

    [UnityTest]
    public IEnumerator Navigator_OnDualBakedNavMeshes_CrawlsUnderCeilingAndStandsAfterExit()
    {
        EnemyNavigator navigator = BuildPostureEnemyOnLowCeilingCorridor(
            new Vector3(0f, 0f, -6f),
            out EnemyConfig config,
            out EnemyPostureController postureController,
            out NavMeshAgent agent);

        Transform actor = navigator.transform;
        int standingAgentTypeId = agent.agentTypeID;
        Vector3 destination = new Vector3(0f, 0f, 6f);
        bool observedCrawling = false;

        yield return WaitForCondition(
            () =>
            {
                navigator.TryMoveTo(destination, config.chaseSpeed);
                observedCrawling |=
                    postureController.CurrentPosture ==
                    EnemyPosture.Crawling;
                return actor.position.z > 4.5f;
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

    // Two things the flank and the retreat depend on for a destination that
    // only exists under a low ceiling.
    //
    // The tactical sampler asked with the current posture alone, so a
    // crawl-only spot was thrown out before the route planner - the one part
    // of this that does drop to a crawl - was ever asked about it, and the
    // crawl fallback could not be reached by the destinations that needed it.
    //
    // Only NavMesh.CalculatePath consumes the path-query allowance. Rejecting
    // standing at destination sampling is free, so the one actual crawl path
    // still fits into a one-query grant.
    [UnityTest]
    public IEnumerator TacticalPlanning_ForACrawlOnlyDestination_SamplesItAndChargesTheCalculatedPath()
    {
        EnemyNavigator navigator = BuildPostureEnemyOnLowCeilingCorridor(
            new Vector3(0f, 0f, -6f),
            out _,
            out EnemyPostureController postureController,
            out _);

        yield return null;

        Assert.That(
            postureController.CurrentPosture,
            Is.EqualTo(EnemyPosture.Standing),
            "This test needs the enemy standing outside the low passage.");

        // Mid-passage: the ceiling spans z -2.5 to 2.5, which is further from
        // the standing NavMesh than the navigator's sample radius reaches.
        Assert.That(
            navigator.TrySampleTacticalPoint(
                Vector3.zero,
                1f,
                out Vector3 sampled),
            Is.True,
            "A crawl-only destination was refused before the route planner saw it.");
        Assert.That(Mathf.Abs(sampled.z), Is.LessThan(2.5f));

        EnemyTacticalNavigationPlanner tacticalPlanner = new(navigator);

        // Standing is rejected before CalculatePath; the crawl calculation is
        // the only path query charged for this endpoint.
        int pathBudget = 1;

        Assert.That(
            tacticalPlanner.TryPlanRoute(
                sampled,
                ref pathBudget,
                out EnemyTacticalRoute route,
                out bool budgetExhausted),
            Is.True);
        Assert.That(budgetExhausted, Is.False);
        Assert.That(pathBudget, Is.Zero);

        pathBudget = 2;

        Assert.That(
            tacticalPlanner.TryPlanRoute(
                sampled,
                ref pathBudget,
                out route,
                out budgetExhausted),
            Is.True,
            "The crawl fallback did not find the route the enemy walks above.");
        Assert.That(budgetExhausted, Is.False);
        Assert.That(
            pathBudget,
            Is.EqualTo(1),
            "The crawl fallback did not charge for its query.");

        // Planning lands the destination on the NavMesh before routing to it,
        // so the route ends somewhere other than the point asked for. The
        // planners read that off the last corner and walk to it: approving a
        // route to one spot and then moving to another is how a flank arrived
        // somewhere it had never checked.
        Vector3 shortOfTheWall = new(2.6f, 0f, 0f);
        pathBudget = 2;

        Assert.That(
            tacticalPlanner.TryPlanRoute(
                shortOfTheWall,
                ref pathBudget,
                out route,
                out budgetExhausted),
            Is.True);

        Vector3 endpoint = route.Endpoint;

        Assert.That(
            Mathf.Abs(endpoint.x),
            Is.LessThan(2.4f),
            "The route ended outside the corridor walls.");
        Assert.That(
            Vector3.Distance(endpoint, shortOfTheWall),
            Is.GreaterThan(0.1f),
            "This case needs a destination the planner has to move.");

        // And how far it may be moved is the move's own landing radius, not a
        // wider one of the preview's. A point well beyond it is refused here
        // exactly as the move would refuse it - the preview used to snap such
        // a point back through a wall and approve the route to it.
        Vector3 throughTheWall = new(5f, 0f, 0f);
        pathBudget = 2;

        Assert.That(
            tacticalPlanner.TryPlanRoute(
                throughTheWall,
                ref pathBudget,
                out route,
                out budgetExhausted),
            Is.False,
            "The preview landed a destination the move would not have.");
        Assert.That(budgetExhausted, Is.False);
    }

    // The other way round from the test above, and the one the tactical
    // planner used to get wrong. A crawling enemy asked about the crawling
    // route and stopped there, while EnemyPostureTraversalPlanner puts it back
    // on its feet as soon as its recovery check comes round - so the route the
    // visibility check approved was the one the enemy was about to stop using.
    // The preview must expose the same standing prefix and crawl continuation
    // as runtime, including the height used to check visibility on each leg.
    [UnityTest]
    public IEnumerator TacticalPlanning_WhileCrawling_BuildsStandingThenCrawlSequence()
    {
        EnemyNavigator navigator = BuildPostureEnemyOnLowCeilingCorridor(
            new Vector3(0f, 0f, -6f),
            out EnemyConfig config,
            out EnemyPostureController postureController,
            out NavMeshAgent agent);

        Assert.That(
            postureController.TrySetServerPosture(EnemyPosture.Crawling),
            Is.True,
            "The enemy could not be put onto its belly for this test.");

        yield return null;

        Assert.That(
            postureController.CurrentPosture,
            Is.EqualTo(EnemyPosture.Crawling));

        // Out in the open part of the corridor, so getting up is a question of
        // when the recovery check comes round rather than whether it can.
        Assert.That(
            postureController.CanUsePostureAtCurrentPosition(
                EnemyPosture.Standing),
            Is.True,
            "This test needs somewhere the enemy could stand up.");

        Assert.That(
            navigator.TrySampleTacticalPoint(
                Vector3.zero,
                1f,
                out Vector3 underTheCeiling),
            Is.True);

        EnemyTacticalNavigationPlanner tacticalPlanner = new(navigator);

        // One query buys the standing attempt, which cannot reach a spot under
        // a low ceiling. Coming back with "no route" would be a verdict on
        // evidence never gathered.
        int pathBudget = 1;

        Assert.That(
            tacticalPlanner.TryPlanRoute(
                underTheCeiling,
                ref pathBudget,
                out EnemyTacticalRoute route,
                out bool budgetExhausted),
            Is.False);
        Assert.That(
            budgetExhausted,
            Is.True,
            "A crawling enemy planned without trying to stand up first.");

        // The waypoint scan may span frames under the same small allowance.
        // It must make progress rather than restarting at the far endpoint.
        bool found = false;

        for (int attempt = 0; attempt < 16 && !found; attempt++)
        {
            pathBudget = 4;
            found = tacticalPlanner.TryPlanRoute(
                underTheCeiling,
                ref pathBudget,
                out route,
                out budgetExhausted);

            if (!found)
            {
                Assert.That(
                    budgetExhausted,
                    Is.True,
                    "A resumable waypoint scan returned a navigation failure.");
            }
        }

        Assert.That(found, Is.True, "The standing waypoint scan did not finish.");
        Assert.That(budgetExhausted, Is.False);
        Assert.That(route.Legs.Count, Is.EqualTo(2));
        Assert.That(route.Legs[0].Posture, Is.EqualTo(EnemyPosture.Standing));
        Assert.That(route.Legs[1].Posture, Is.EqualTo(EnemyPosture.Crawling));
        Assert.That(
            route.Legs[0].BodyHeight,
            Is.EqualTo(config.standingAgentHeight).Within(0.001f));
        Assert.That(
            route.Legs[1].BodyHeight,
            Is.EqualTo(config.crawlingAgentHeight).Within(0.001f));
        Assert.That(
            Vector3.Distance(route.Endpoint, route.Legs[1].Endpoint),
            Is.LessThan(0.05f));
        Assert.That(
            Vector3.Distance(route.Legs[0].Endpoint, route.Endpoint),
            Is.GreaterThan(0.5f),
            "The standing prefix was collapsed into the final crawl endpoint.");

        // A non-zero transition used to lose the prefix: after standing up,
        // the normal repath rebuilt from the final destination and immediately
        // put the enemy back onto its belly at the original position.
        Vector3 previewStandingEndpoint = route.Legs[0].Endpoint;
        config.PostureProfile.crawlingToStandingTransitionDuration = 0.05f;

        Assert.That(
            navigator.TryMoveTo(underTheCeiling, config.chaseSpeed),
            Is.False,
            "The posture change should finish before the path is applied.");
        Assert.That(postureController.IsPostureTransitionInProgress, Is.True);
        Assert.That(
            postureController.TargetPosture,
            Is.EqualTo(EnemyPosture.Standing));

        yield return new WaitForSeconds(0.06f);
        yield return null;

        Assert.That(
            postureController.CurrentPosture,
            Is.EqualTo(EnemyPosture.Standing));
        Assert.That(
            navigator.TryMoveTo(underTheCeiling, config.chaseSpeed),
            Is.True,
            "Runtime did not resume the standing prefix after the transition.");

        Vector3[] runtimeCorners = agent.path.corners;
        Assert.That(runtimeCorners.Length, Is.GreaterThan(1));
        Assert.That(
            Vector3.Distance(
                runtimeCorners[runtimeCorners.Length - 1],
                previewStandingEndpoint),
            Is.LessThan(0.1f),
            "Runtime applied a different first leg from the tactical preview.");
    }

    // And the window in between, which is where the two planners used to
    // disagree. For the second or so a crawl is held for, the move will not
    // try to get up however clear the ceiling is, so neither may the plan -
    // judging a standing route the enemy is not about to walk is the same
    // mistake as judging a crawling one it is about to leave.
    [UnityTest]
    public IEnumerator TacticalPlanning_WithinTheCrawlCooldown_StaysOnTheCrawlingRoute()
    {
        EnemyNavigator navigator = BuildPostureEnemyOnLowCeilingCorridor(
            new Vector3(0f, 0f, -6f),
            out EnemyConfig config,
            out EnemyPostureController postureController,
            out _);

        config.PostureProfile.minPostureDuration = 5f;

        Assert.That(
            postureController.TrySetServerPosture(EnemyPosture.Crawling),
            Is.True);

        yield return null;

        Assert.That(
            postureController.CurrentPosture,
            Is.EqualTo(EnemyPosture.Crawling));
        Assert.That(
            postureController.CanUsePostureAtCurrentPosition(
                EnemyPosture.Standing),
            Is.True,
            "This test needs a ceiling that is not what keeps it down.");

        // Clear overhead, but only just onto its belly, so the move would keep
        // crawling and so must the plan.
        Assert.That(
            postureController.GetPreferredPlanningPosture(),
            Is.EqualTo(EnemyPosture.Crawling));

        Assert.That(
            navigator.TrySampleTacticalPoint(
                Vector3.zero,
                1f,
                out Vector3 underTheCeiling),
            Is.True);

        // One query is enough when the first posture tried is the right one.
        EnemyTacticalNavigationPlanner tacticalPlanner = new(navigator);
        int pathBudget = 1;

        Assert.That(
            tacticalPlanner.TryPlanRoute(
                underTheCeiling,
                ref pathBudget,
                out EnemyTacticalRoute route,
                out bool budgetExhausted),
            Is.True,
            "The plan spent its query standing up while the move would crawl.");
        Assert.That(budgetExhausted, Is.False);
        Assert.That(pathBudget, Is.Zero);

        // And when the crawl destination cannot even be sampled, that is the
        // answer. No CalculatePath was issued and standing must remain untried.
        Vector3 outsideTheCorridor = new(20f, 0f, 0f);
        pathBudget = 2;

        Assert.That(
            tacticalPlanner.TryPlanRoute(
                outsideTheCorridor,
                ref pathBudget,
                out route,
                out budgetExhausted),
            Is.False);
        Assert.That(budgetExhausted, Is.False);
        Assert.That(
            pathBudget,
            Is.EqualTo(2),
            "A held crawl still went on to plan a standing route.");
    }

    // Hidden lying down, in plain view the moment it gets up - and the route
    // that begins with getting up used to be called hidden, because the scan
    // starts at the first step of the first segment and never looks at the
    // corner it sets off from. Nothing else covers that spot either: the
    // perception cache measures where the enemy is at the height it has now,
    // which is the crawl it is about to leave.
    [UnityTest]
    public IEnumerator RouteVisibility_WhenSettingOffMeansStandingUp_ChecksTheStartingPoint()
    {
        yield return StartHost();

        EnemyNavigator navigator = BuildPostureEnemyOnLowCeilingCorridor(
            new Vector3(0f, 0f, -6f),
            out _,
            out EnemyPostureController postureController,
            out _);

        // Both are built after the NavMesh so they block sight without
        // carving the floor the enemy plans over.
        //
        // Waist-high between the watcher and the enemy: it hides a body lying
        // down and not one standing up. Then head-high just beyond the enemy,
        // so everywhere the route goes is hidden at any height. Only the
        // corner it sets off from is exposed, and only once it stands.
        CreateGeometry(
            "Low barrier",
            new Vector3(0f, 0.7f, -4.5f),
            new Vector3(4.6f, 1.4f, 0.3f));
        CreateGeometry(
            "High barrier",
            new Vector3(0f, 1.5f, -6.8f),
            new Vector3(4.6f, 3f, 0.3f));

        EnemyTarget watcher = CreateSpawnedTarget(
            new Vector3(0f, 0f, -3f),
            canBeDetected: true,
            withGaze: true);
        watcher.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        Physics.SyncTransforms();

        Assert.That(
            postureController.TrySetServerPosture(EnemyPosture.Crawling),
            Is.True);

        yield return null;

        Assert.That(
            postureController.CurrentPosture,
            Is.EqualTo(EnemyPosture.Crawling));

        Vector3 start = navigator.transform.position;
        float crawlHeight =
            navigator.GetBodyHeightForPosture(EnemyPosture.Crawling);
        float standHeight =
            navigator.GetBodyHeightForPosture(EnemyPosture.Standing);

        // The whole point of the fixture. If the barrier hides neither posture
        // or both, the test below proves nothing, so say so here.
        Assert.That(
            PlayerGazeNetwork.IsBodySeenByAnyone(start, crawlHeight),
            Is.False,
            "Fixture: the enemy should be hidden while it is lying down.");
        Assert.That(
            PlayerGazeNetwork.IsBodySeenByAnyone(start, standHeight),
            Is.True,
            "Fixture: the enemy should be seen once it stands up.");

        // The route runs off behind the head-high barrier, so every sample
        // along it is hidden and the only thing that can fail the check is the
        // corner it starts on. Without that look the whole route reads as
        // hidden, which is the bug.
        Assert.That(
            postureController.GetPreferredPlanningPosture(),
            Is.EqualTo(EnemyPosture.Standing));

        EnemyTacticalNavigationPlanner planner = new(navigator);
        Vector3 behindTheBarrier = new(0f, 0f, -7.4f);
        int pathBudget = 4;

        Assert.That(
            planner.TryPlanRoute(
                behindTheBarrier,
                ref pathBudget,
                out EnemyTacticalRoute route,
                out _),
            Is.True);
        Assert.That(
            PlayerGazeNetwork.IsBodySeenByAnyone(route.Endpoint, standHeight),
            Is.False,
            "Fixture: the far end of the route should be hidden even standing.");
        Assert.That(route.Legs.Count, Is.GreaterThan(0));
        Assert.That(
            route.Legs[0].Posture,
            Is.EqualTo(EnemyPosture.Standing));
        Assert.That(route.StartsWithPostureChange, Is.True);

        int visibilityBudget = 32;
        EnemyRouteScan scan = default;

        Assert.That(
            planner.IsRouteHidden(
                route,
                1.5f,
                ref visibilityBudget,
                ref scan,
                out bool budgetExhausted),
            Is.False,
            "A route that begins by standing into somebody's view was called hidden.");
        Assert.That(budgetExhausted, Is.False);
        Assert.That(
            visibilityBudget,
            Is.LessThan(32),
            "The starting point was never looked at.");
    }

    [UnityTest]
    public IEnumerator DoorTraversal_OnLShapedRoute_IgnoresDoorOutsideCalculatedCorridor()
    {
        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeLCorridor(standingAgentTypeId, crawlingAgentTypeId);

        EnemyDoorInteractionConfig doorConfig =
            Track(ScriptableObject.CreateInstance<EnemyDoorInteractionConfig>());
        PlayModeTestReflection.SetField(doorConfig, "detectionRadius", 20f);
        PlayModeTestReflection.SetField(doorConfig, "pathHalfWidth", 0.5f);
        PlayModeTestReflection.SetField(
            doorConfig,
            "interactionDistance",
            0.5f);
        PlayModeTestReflection.SetField(
            doorConfig,
            "usePhysicsOverlapFallback",
            false);

        CreateEnemyDoor(
            new Vector3(0f, 0f, 2f),
            out EnemyDoorInteractionZone routeDoor);
        CreateEnemyDoor(
            new Vector3(3f, 0f, 2f),
            out EnemyDoorInteractionZone chordDoor);

        GameObject actor = Track(new GameObject("L-route door evaluator"));
        EnemyDoorInteractor interactor =
            actor.AddComponent<EnemyDoorInteractor>();
        PlayModeTestReflection.SetField(interactor, "config", doorConfig);
        Vector3 current = Vector3.zero;
        Vector3 destination = new(5f, 0f, 5f);
        NavMeshQueryFilter filter = new()
        {
            agentTypeID = standingAgentTypeId,
            areaMask = NavMesh.AllAreas
        };
        NavMeshPath route = new();

        Assert.That(
            NavMesh.CalculatePath(current, destination, filter, route),
            Is.True);
        Assert.That(route.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
        Assert.That(
            route.corners.Length,
            Is.GreaterThanOrEqualTo(3),
            "Test setup did not produce an L-shaped NavMesh route.");

        EnemyDoorNavigationResult directChordResult =
            interactor.EvaluateNavigation(current, destination);
        Assert.That(directChordResult.HasOverrideDestination, Is.True);
        Assert.That(
            Vector3.Distance(
                directChordResult.OverrideDestination,
                chordDoor.GetInteractionPointFor(current)),
            Is.LessThan(
                Vector3.Distance(
                    directChordResult.OverrideDestination,
                    routeDoor.GetInteractionPointFor(current))),
            "Test setup did not place the decoy door on the direct chord.");

        EnemyDoorNavigationResult result = interactor.EvaluateNavigation(
            current,
            destination,
            route.corners);

        Assert.That(result.HasOverrideDestination, Is.True);
        Vector3 routeDoorPoint = routeDoor.GetInteractionPointFor(current);
        Vector3 chordDoorPoint = chordDoor.GetInteractionPointFor(current);
        Assert.That(
            Vector3.Distance(result.OverrideDestination, routeDoorPoint),
            Is.LessThan(
                Vector3.Distance(
                    result.OverrideDestination,
                    chordDoorPoint)),
            "Door traversal selected a door on the direct chord instead of the calculated L-shaped route.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator RepathScheduler_MultipleEnemies_StayWithinPathQueryBudget()
    {
        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        EnemyConfig config = CloneNavigationConfig(enemyConfig);
        config.NavigationProfile.repathInterval = 0.2f;
        config.NavigationProfile.destinationRepathDistance = 0.3f;
        config.NavigationProfile.maximumPathQueriesPerRepath = 4;
        const int enemyCount = 8;
        List<EnemyNavigator> navigators = new(enemyCount);

        for (int i = 0; i < enemyCount; i++)
        {
            float x = -3.5f + i;
            GameObject actor = Track(new GameObject($"Query budget enemy {i}"));
            actor.transform.position = new Vector3(x, 0f, -5f);
            NavMeshAgent agent = actor.AddComponent<NavMeshAgent>();
            agent.agentTypeID = standingAgentTypeId;
            agent.speed = 3f;
            agent.acceleration = 20f;
            EnemyNavigator navigator = actor.AddComponent<EnemyNavigator>();

            Assert.That(
                TryPlaceAgent(agent, actor.transform.position),
                Is.True,
                $"Query budget enemy {i} could not be placed on NavMesh.");
            navigator.Configure(config);
            navigators.Add(navigator);
        }

        for (int frame = 0; frame < 45; frame++)
        {
            float jitter = Mathf.Sin(frame * 0.25f) * 0.05f;

            for (int i = 0; i < navigators.Count; i++)
            {
                Vector3 destination = new(
                    -3.5f + i + jitter,
                    0f,
                    5f);
                Assert.That(
                    navigators[i].TryMoveTo(destination, 3f),
                    Is.True);
            }

            yield return null;
        }

        for (int i = 0; i < navigators.Count; i++)
        {
            EnemyNavigationQueryTelemetrySnapshot telemetry =
                navigators[i].QueryTelemetry;
            Assert.That(
                telemetry.PathQueries,
                Is.LessThanOrEqualTo(2),
                $"Enemy {i} exceeded the repath query budget while its target stayed inside the destination threshold.");
            Assert.That(telemetry.BudgetRejections, Is.Zero);
            Assert.That(telemetry.ReusedPaths, Is.GreaterThan(0));
        }
    }

    [UnityTest]
    public IEnumerator Navigator_ChasingMovingTargetNearClutter_DoesNotHaltOutsideOfAttack()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        // Scattered, non-sealing clutter along the chase area — close to
        // a real room. None of these fully block any route; they just
        // sit in the way sometimes, which is what exposed the posture
        // planner sampling a moving destination with too tight a radius
        // and periodically misjudging that standing was impossible.
        Vector3[] clutterPositions =
        {
            new Vector3(2f, 0f, 3f),
            new Vector3(-2.5f, 0f, -1f),
            new Vector3(1f, 0f, -4f),
            new Vector3(-1.5f, 0f, 6f),
        };

        foreach (Vector3 clutterPos in clutterPositions)
        {
            NetworkItemTestDraggable clutterItem = CreateSpawnedNavigationItem(
                clutterPos,
                makeDynamic: true,
                useGravity: true,
                colliderSize: new Vector3(1.2f, 1f, 1.2f));
            ItemNavigationObstacle clutterNav =
                clutterItem.GetComponent<ItemNavigationObstacle>();

            yield return WaitForCondition(
                () => clutterNav.IsBlockingNavigation &&
                      clutterItem.GetComponent<NavMeshObstacle>().carving,
                "Clutter item did not carve.");
        }

        yield return new WaitForSecondsRealtime(0.5f);

        GameplayNoiseWorldService noiseWorld = CreateNoiseWorld();
        EnemyTarget target = CreateSpawnedTarget(
            new Vector3(0f, 0f, 5f),
            canBeDetected: true);

        NetworkEnemyController enemy = CreateSpawnedProductionEnemy(
            enemyPrefab,
            noiseWorld,
            new Vector3(0f, 0f, -5f));
        EnemyServerRuntime runtime = enemy.GetComponent<EnemyServerRuntime>();
        NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
        EnemyPostureController posture =
            enemy.GetComponent<EnemyPostureController>();
        EnemyNavigator navigator = enemy.GetComponent<EnemyNavigator>();

        yield return WaitForCondition(
            () => runtime.IsRunning && agent.enabled && agent.isOnNavMesh,
            "Enemy did not start on the prebuilt NavMesh.");

        // Acquisition is what this waits for, not chasing specifically. A
        // target first seen from beyond the stalking threshold is stalked, and
        // insisting on Chase here would only be waiting for the target's
        // wandering to bring it close enough.
        yield return WaitForCondition(
            () => enemy.HasTarget &&
                  (enemy.CurrentState == EnemyState.Chase ||
                   enemy.CurrentState == EnemyState.Attack ||
                   enemy.CurrentState == EnemyState.Stalk),
            "Enemy did not acquire the moving target.");

        // A continuously shifting destination, unlike a fixed
        // back-and-forth point, mirrors a real player being chased.
        float startTime = Time.realtimeSinceStartup;
        float endTime = startTime + 20f;
        bool wasMoving = false;
        int nonAttackStopEvents = 0;

        // A halt has to last to be a halt. Measured in seconds rather than in
        // frames because this loop runs unrendered at well over a thousand
        // frames a second and the rate swings by a third between runs - the
        // same frame count was twenty milliseconds in one run and would be
        // half a second at playable frame rates.
        //
        // A quarter second is far longer than the repath at the start of a
        // chase, which leaves the agent without a steering target for about
        // twenty milliseconds, and far shorter than the periodic stalls this
        // test was written to catch.
        const float haltGraceSeconds = 0.25f;

        List<string> stopReports = new();
        float worstFrameMs = 0f;
        bool inHalt = false;
        bool haltCounted = false;
        float haltSeconds = 0f;
        string haltOpening = null;
        int totalFrames = 0;
        int framesBelowThreshold = 0;
        float minSpeed = float.PositiveInfinity;

        while (Time.realtimeSinceStartup < endTime)
        {
            float t = Time.time;
            target.transform.position = new Vector3(
                Mathf.Sin(t * 0.5f) * 6f,
                0f,
                2f + Mathf.Cos(t * 0.35f) * 8f);
            Physics.SyncTransforms();

            float frameMs = Time.unscaledDeltaTime * 1000f;
            worstFrameMs = Mathf.Max(worstFrameMs, frameMs);

            float speed = agent.enabled ? agent.velocity.magnitude : 0f;
            bool isMoving = speed > 0.15f;

            totalFrames++;
            minSpeed = Mathf.Min(minSpeed, speed);

            if (!isMoving)
            {
                framesBelowThreshold++;
            }

            // Standing still is correct in three states now. Attacking has
            // always been one; stalking is the second, and its whole purpose is
            // to stop and watch, so counting it as a stall would test the
            // opposite of what is wanted. Lying in ambush is the third - it
            // stops navigation on entry and waits for the target to turn
            // round, so every ambush is a halt by design.
            // Shoving clutter aside is the fourth. It holds the agent for
            // barricadeShoveStopDuration, which is 0.4s against this test's
            // quarter-second tolerance - and this test scatters clutter on
            // purpose, so a chase that never shoved anything would be the
            // surprising outcome.
            bool shouldBeMoving =
                enemy.CurrentState != EnemyState.Attack &&
                enemy.CurrentState != EnemyState.Stalk &&
                enemy.CurrentState != EnemyState.Ambush &&
                !navigator.IsHeldForForcefulPush;

            bool halting = !isMoving && shouldBeMoving;

            if (halting && (inHalt || wasMoving))
            {
                if (!inHalt)
                {
                    inHalt = true;
                    haltCounted = false;
                    haltSeconds = 0f;
                    haltOpening =
                        $"at {Time.realtimeSinceStartup - startTime:0.0}s " +
                        $"speed={speed:0.###} isStopped={agent.isStopped} " +
                        $"pathPending={agent.pathPending} " +
                        $"hasPath={agent.hasPath} status={agent.pathStatus} " +
                        $"remaining={agent.remainingDistance:0.##} " +
                        $"state={enemy.CurrentState} " +
                        $"posture={posture.CurrentPosture} " +
                        $"transitioning={posture.IsPostureTransitionInProgress} " +
                        $"shoving={navigator.IsHeldForForcefulPush}";
                }

                haltSeconds += Time.unscaledDeltaTime;

                if (!haltCounted && haltSeconds >= haltGraceSeconds)
                {
                    nonAttackStopEvents++;
                    haltCounted = true;
                    stopReports.Add($"[{haltOpening} held for {haltSeconds:0.00}s]");
                }
            }
            else
            {
                inHalt = false;
            }

            wasMoving = isMoving;

            yield return null;
        }

        // Most of the frames under the threshold are the enemy standing still
        // to attack, which is why they are reported next to the stops rather
        // than counted as any.
        Assert.That(
            nonAttackStopEvents,
            Is.Zero,
            $"Enemy stood still for {haltGraceSeconds:0.00}s or more mid-chase " +
            $"outside of an attack. {totalFrames} frames, worst " +
            $"{worstFrameMs:0.#} ms, {framesBelowThreshold} under the speed " +
            $"threshold, slowest {minSpeed:0.###}. " +
            string.Join(" ", stopReports));
    }

    // An enemy standing in the scene before the match cannot be varied by
    // difficulty or added partway through one, which the doll being picked up
    // will need. This is that path: the spawner puts one into the world while
    // the match is already running.
    [UnityTest]
    public IEnumerator Spawner_PutsALiveEnemyWhereTheSpawnPointSaysAndTakesItBack()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        NetworkEnemySpawner spawner = CreateSpawnedEnemySpawner();

        GameObject pointObject = Track(new GameObject("Enemy spawn point"));
        pointObject.transform.SetPositionAndRotation(
            new Vector3(2f, 0f, -1f),
            Quaternion.Euler(0f, 90f, 0f));
        EnemySpawnPoint spawnPoint = pointObject.AddComponent<EnemySpawnPoint>();
        spawnPoint.ConfigureEditor(
            enemyPrefab.GetComponent<NetworkEnemyController>(),
            route: null);

        Assert.That(
            spawner.TrySpawnServerOnly(spawnPoint, out NetworkEnemyController enemy),
            Is.True);
        Assert.That(enemy, Is.Not.Null);
        Track(enemy.gameObject);

        Assert.That(enemy.GetComponent<NetworkObject>().IsSpawned, Is.True);
        Assert.That(
            Vector3.Distance(enemy.transform.position, spawnPoint.Position),
            Is.LessThan(0.01f));
        Assert.That(spawner.SpawnedEnemies, Has.Count.EqualTo(1));

        // The config comes from the prefab here; with a session running it
        // would be the difficulty the host picked.
        Assert.That(enemy.Config, Is.Not.Null);

        spawner.DespawnAllServerOnly();
        yield return null;

        Assert.That(spawner.SpawnedEnemies, Is.Empty);
    }

    [UnityTest]
    public IEnumerator Spawner_FillsEverySpawnPointTheMapDeclares()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        GameObject mapRootObject = Track(new GameObject("Map root"));
        GameMapRoot mapRoot = mapRootObject.AddComponent<GameMapRoot>();

        for (int i = 0; i < 2; i++)
        {
            GameObject point = new($"Enemy spawn {i}");
            point.transform.SetParent(mapRootObject.transform, false);
            point.transform.position = new Vector3(i * 3f, 0f, 2f);
            point.AddComponent<EnemySpawnPoint>().ConfigureEditor(
                enemyPrefab.GetComponent<NetworkEnemyController>(),
                route: null);
        }

        NetworkEnemySpawner spawner = CreateSpawnedEnemySpawner();
        PlayModeTestReflection.SetField(
            spawner,
            "gameMapService",
            new SpawnerMapSessionStub(mapRoot));

        Assert.That(spawner.SpawnMapEnemiesServerOnly(), Is.EqualTo(2));
        Assert.That(spawner.SpawnedEnemies, Has.Count.EqualTo(2));

        for (int i = 0; i < spawner.SpawnedEnemies.Count; i++)
        {
            NetworkEnemyController spawned = spawner.SpawnedEnemies[i];
            Track(spawned.gameObject);
            Assert.That(spawned.GetComponent<NetworkObject>().IsSpawned, Is.True);
        }

        spawner.DespawnAllServerOnly();
        yield return null;
    }

    private NetworkEnemySpawner CreateSpawnedEnemySpawner()
    {
        GameObject host = Track(new GameObject("Enemy spawner"));
        host.SetActive(false);

        NetworkObject networkObject = host.AddComponent<NetworkObject>();
        NetworkEnemySpawner spawner = host.AddComponent<NetworkEnemySpawner>();

        // Off by default: this fixture has no session scope to resolve a map
        // service from, so the tests drive the spawner directly.
        PlayModeTestReflection.SetField(spawner, "spawnMapEnemiesWhenReady", false);

        host.SetActive(true);
        networkObject.Spawn(true);

        Assert.That(networkObject.IsSpawned, Is.True);
        return spawner;
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

        // Registered so the enemy can be spawned at runtime rather than only
        // placed in a scene beforehand.
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = enemyPrefab });
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
        bool canBeDetected,
        bool withGaze = false)
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

#if UNITY_EDITOR
        LogAssert.Expect(
            LogType.Error,
            new Regex("EnemyTarget has invalid visibility configuration:"));
#endif
        EnemyTarget target = targetObject.AddComponent<EnemyTarget>();
        PlayModeTestReflection.SetField(
            target,
            "visibilityColliders",
            new Collider[] { collider });
        PlayModeTestReflection.SetField(
            target,
            "canBeDetected",
            canBeDetected);

        // Before the spawn, or OnNetworkSpawn never runs for it and it never
        // reaches the registry the enemy asks - which reads as a target that
        // can never see anything.
        if (withGaze)
        {
            targetObject.AddComponent<PlayerGazeNetwork>();
        }

        targetObject.SetActive(true);

        PlayModeTestReflection.SetField(
            networkObject,
            "NetworkManagerOwner",
            manager);
        networkObject.Spawn();

        Assert.That(networkObject.IsSpawned, Is.True);
        return target;
    }

    private NetworkItemTestDraggable CreateSpawnedNavigationItem(
        Vector3 position,
        bool makeDynamic = false,
        bool useGravity = false,
        Vector3? colliderSize = null)
    {
        DraggableObjectData itemData =
            Track(ScriptableObject.CreateInstance<DraggableObjectData>());
        itemData.BlocksEnemyNavigation = true;
        itemData.Mass = 5f;
        itemData.MaxFollowSpeed = 15f;
        itemData.FollowSpeedMultiplier = 2f;
        itemData.MaxDragDistance = 6f;
        itemData.ThrowVelocitySamples = 3f;
        itemData.MinDistance = 1f;

        GameObject itemObject = Track(new GameObject("Enemy blocking item"));
        itemObject.SetActive(false);
        itemObject.layer = 10;
        itemObject.transform.position = position;

        NetworkObject networkObject = itemObject.AddComponent<NetworkObject>();
        Rigidbody body = itemObject.AddComponent<Rigidbody>();
        body.useGravity = useGravity;
        body.isKinematic = !makeDynamic;
        body.constraints = RigidbodyConstraints.FreezeRotation;

        if (!useGravity)
            body.constraints |= RigidbodyConstraints.FreezePositionY;

        BoxCollider collider = itemObject.AddComponent<BoxCollider>();
        collider.size = colliderSize ?? new Vector3(2f, 1.5f, 2f);
        collider.center = new Vector3(0f, 0.75f, 0f);

        NetworkItemTestDraggable item =
            itemObject.AddComponent<NetworkItemTestDraggable>();
        PlayModeTestReflection.SetField(item, "data", itemData);
        itemObject.SetActive(true);

        PlayModeTestReflection.SetField(
            networkObject,
            "NetworkManagerOwner",
            manager);
        networkObject.Spawn();

        Assert.That(networkObject.IsSpawned, Is.True);
        Assert.That(
            itemObject.GetComponent<ItemNavigationObstacle>(),
            Is.Not.Null,
            "DraggableObject did not compose its navigation adapter.");
        return item;
    }

    private HidingPlaceNavigationObstacle
        CreateSpawnedHidingNavigationObstacle(Vector3 position)
    {
        GameObject hidingPlace = Track(
            new GameObject("Enemy blocking hiding place"));
        hidingPlace.SetActive(false);
        hidingPlace.layer = 10;
        hidingPlace.transform.position = position;

        NetworkObject networkObject =
            hidingPlace.AddComponent<NetworkObject>();
        BoxCollider collider = hidingPlace.AddComponent<BoxCollider>();
        collider.size = new Vector3(2f, 1.5f, 2f);
        collider.center = new Vector3(0f, 0.75f, 0f);
        HidingPlaceNavigationObstacle navigation =
            hidingPlace.AddComponent<HidingPlaceNavigationObstacle>();
        hidingPlace.SetActive(true);

        PlayModeTestReflection.SetField(
            networkObject,
            "NetworkManagerOwner",
            manager);
        networkObject.Spawn();

        Assert.That(networkObject.IsSpawned, Is.True);
        Assert.That(
            hidingPlace.GetComponent<NavMeshObstacle>(),
            Is.Not.Null,
            "Hiding place did not compose its NavMesh obstacle.");
        return navigation;
    }

    private HidingPlaceInteractable CreateSpawnedHidingPlace(Vector3 position)
    {
        HidingPlaceData settings =
            Track(ScriptableObject.CreateInstance<HidingPlaceData>());
        // Instant entry, matching the shipped configuration: nothing here may
        // depend on the Entering state surviving a frame.
        PlayModeTestReflection.SetField(settings, "enterDuration", 0f);
        PlayModeTestReflection.SetField(settings, "exitDuration", 0.1f);

        GameObject placeObject =
            Track(new GameObject("Investigated hiding place"));
        placeObject.SetActive(false);
        placeObject.layer = 10;
        placeObject.transform.position = position;

        NetworkObject networkObject =
            placeObject.AddComponent<NetworkObject>();
        BoxCollider collider = placeObject.AddComponent<BoxCollider>();
        collider.size = new Vector3(0.8f, 0.8f, 0.8f);
        collider.center = new Vector3(0f, 0.4f, 0f);

        // The interaction anchor stands in front of the box the way an authored
        // one does - inside it, the NavMesh carve would make it unreachable.
        // It also has to sit near the floor, because it doubles as the position
        // enemies navigate to: the authored one is at the box's base, and an
        // anchor left floating produces a destination no agent can path to.
        // Just clear of the floor plane, so the entry line-of-sight ray from
        // the player's capsule centre descends without grazing it.
        Transform interactionAnchor = CreateHidingAnchor(
            placeObject.transform,
            "Interaction Anchor",
            new Vector3(0f, 0.1f, -1.2f));
        Transform hidingPoint = CreateHidingAnchor(
            placeObject.transform,
            "Hiding Point",
            Vector3.zero);
        Transform cameraAnchor = CreateHidingAnchor(
            placeObject.transform,
            "Camera Anchor",
            Vector3.up);
        Transform exitPoint = CreateHidingAnchor(
            placeObject.transform,
            "Exit Point",
            new Vector3(0f, 0f, -2f));

        HidingPlaceInteractable hidingPlace =
            placeObject.AddComponent<HidingPlaceInteractable>();
        PlayModeTestReflection.SetField(hidingPlace, "data", settings);
        PlayModeTestReflection.SetField(
            hidingPlace,
            "interactionAnchor",
            interactionAnchor);
        PlayModeTestReflection.SetField(
            hidingPlace,
            "hidingPoint",
            hidingPoint);
        PlayModeTestReflection.SetField(
            hidingPlace,
            "cameraAnchor",
            cameraAnchor);
        PlayModeTestReflection.SetField(hidingPlace, "exitPoint", exitPoint);
        PlayModeTestReflection.SetField(
            hidingPlace,
            "fallbackExitPoints",
            Array.Empty<Transform>());
        placeObject.SetActive(true);

        PlayModeTestReflection.SetField(
            networkObject,
            "NetworkManagerOwner",
            manager);
        networkObject.Spawn();

        Assert.That(networkObject.IsSpawned, Is.True);
        Assert.That(
            hidingPlace.IsAvailable,
            Is.True,
            "Test hiding place rejected entries right after spawning.");
        Physics.SyncTransforms();
        return hidingPlace;
    }

    private PlayerHidingController CreateSpawnedHidingPlayer(Vector3 position)
    {
        GameObject playerObject = Track(new GameObject("Hiding player"));
        playerObject.SetActive(false);
        playerObject.layer = PlayerLayer;
        playerObject.transform.position = position;

        NetworkObject networkObject =
            playerObject.AddComponent<NetworkObject>();
        NetworkTransform networkTransform =
            playerObject.AddComponent<NetworkTransform>();
        networkTransform.AuthorityMode =
            NetworkTransform.AuthorityModes.Owner;

        Rigidbody body = playerObject.AddComponent<Rigidbody>();
        body.useGravity = false;
        CapsuleCollider bodyCollider =
            playerObject.AddComponent<CapsuleCollider>();
        bodyCollider.height = 2f;
        bodyCollider.radius = 0.35f;
        bodyCollider.center = Vector3.up;

        playerObject.AddComponent<HidingEntryEligibilityProbe>();

#if UNITY_EDITOR
        LogAssert.Expect(
            LogType.Error,
            new Regex("EnemyTarget has invalid visibility configuration:"));
#endif
        EnemyTarget enemyTarget = playerObject.AddComponent<EnemyTarget>();
        PlayModeTestReflection.SetField(
            enemyTarget,
            "visibilityColliders",
            new Collider[] { bodyCollider });

        PlayerController movement =
            playerObject.AddComponent<PlayerController>();
        movement.enabled = false;
        PlayerActionGate actionGate =
            playerObject.AddComponent<PlayerActionGate>();

        PlayerHidingController hiding =
            playerObject.AddComponent<PlayerHidingController>();
        PlayModeTestReflection.SetField(
            hiding,
            "networkTransform",
            networkTransform);
        PlayModeTestReflection.SetField(hiding, "playerBody", body);
        PlayModeTestReflection.SetField(hiding, "bodyCollider", bodyCollider);
        PlayModeTestReflection.SetField(hiding, "playerController", movement);
        PlayModeTestReflection.SetField(
            hiding,
            "playerActionGateSource",
            actionGate);
        PlayModeTestReflection.SetField(
            hiding,
            "visualRoot",
            playerObject.transform);
        PlayModeTestReflection.SetField(
            hiding,
            "gameplayColliders",
            new Collider[] { bodyCollider });
        PlayModeTestReflection.SetField(
            hiding,
            "hitboxColliders",
            Array.Empty<Collider>());
        playerObject.SetActive(true);

        PlayModeTestReflection.SetField(
            networkObject,
            "NetworkManagerOwner",
            manager);
        networkObject.SpawnWithOwnership(NetworkManager.ServerClientId);

        Assert.That(networkObject.IsSpawned, Is.True);
        Physics.SyncTransforms();
        return hiding;
    }

    private IEnumerator OccupyHidingPlace(
        HidingPlaceInteractable hidingPlace,
        PlayerHidingController occupant)
    {
        Assert.That(
            hidingPlace.TryRequestEnter(occupant),
            Is.True,
            "Server rejected the hiding entry this fixture is built on.");

        yield return WaitForCondition(
            () => hidingPlace.State == HidingTransitionState.Occupied &&
                  occupant.IsHidden,
            "Fixture player did not become a hidden occupant.");
    }

    private EnemyInvestigateState CreateInvestigateState(
        Vector3 position,
        int standingAgentTypeId,
        out GameObject actor,
        out EnemyBlackboard blackboard,
        out List<EnemyState> stateChanges)
    {
        actor = Track(new GameObject("Investigating enemy"));
        actor.SetActive(false);
        actor.layer = 6;
        actor.transform.position = position;

        NavMeshAgent agent = actor.AddComponent<NavMeshAgent>();
        agent.agentTypeID = standingAgentTypeId;
        agent.radius = 0.5f;
        agent.speed = 4f;
        agent.acceleration = 30f;
        EnemyNavigator navigator = actor.AddComponent<EnemyNavigator>();
        actor.SetActive(true);

        Assert.That(
            TryPlaceAgent(agent, position),
            Is.True,
            "Investigating enemy could not be placed on NavMesh.");

        EnemyConfig config = CloneNavigationConfig(enemyConfig);
        navigator.Configure(config);

        EnemyBlackboard createdBlackboard = new();
        List<EnemyState> observedStateChanges = new();
        blackboard = createdBlackboard;
        stateChanges = observedStateChanges;

        // Investigation only touches the config, the navigator and the
        // blackboard, so the detector, patrol, attack and posture controllers
        // stay null and the state falls back to standing navigation.
        EnemyBrainContext context = new(
            config,
            navigator,
            null,
            null,
            null,
            null,
            createdBlackboard,
            observedStateChanges.Add,
            null);

        return new EnemyInvestigateState(context);
    }

    // Sensors and a detector, but no spawned enemy: a running brain would
    // chase the player and walk into the very line of sight under test.
    private EnemyTargetDetector CreateWatchingDetector(
        Vector3 position,
        GameplayNoiseWorldService noiseWorld)
    {
        GameObject detectorObject = Track(new GameObject("Watching enemy"));
        detectorObject.SetActive(false);
        detectorObject.transform.position = position;

        Transform eyes = CreateHidingAnchor(
            detectorObject.transform,
            "Eyes",
            new Vector3(0f, 1.5f, 0f));

        // OnValidate runs on AddComponent and reports the still-unassigned
        // serialized fields before the test gets a chance to set them.
#if UNITY_EDITOR
        LogAssert.Expect(
            LogType.Error,
            new Regex("EnemyVisionSensor has invalid configuration:"));
#endif
        EnemyVisionSensor vision =
            detectorObject.AddComponent<EnemyVisionSensor>();
        PlayModeTestReflection.SetField(vision, "eyes", eyes);
        PlayModeTestReflection.SetField(
            vision,
            "targetMask",
            (LayerMask)(1 << PlayerLayer));
        PlayModeTestReflection.SetField(
            vision,
            "obstructionMask",
            (LayerMask)(1 << EnvironmentLayer));

        EnemyHearingSensor hearing =
            detectorObject.AddComponent<EnemyHearingSensor>();

#if UNITY_EDITOR
        LogAssert.Expect(
            LogType.Error,
            new Regex("EnemyTargetDetector has invalid configuration:"));
#endif
        EnemyTargetDetector detector =
            detectorObject.AddComponent<EnemyTargetDetector>();
        PlayModeTestReflection.SetField(detector, "visionSensor", vision);
        PlayModeTestReflection.SetField(detector, "hearingSensor", hearing);
        PlayModeTestReflection.SetField(
            detector,
            "stimulusResolverPolicy",
            new EnemyStimulusResolverPolicy());
        detectorObject.SetActive(true);

        detector.Construct(noiseWorld);
        return detector;
    }

    private static IEnumerator TickPerceptionUntil(
        EnemyPerceptionRuntime perception,
        EnemyState state,
        Func<bool> condition,
        string failureMessage)
    {
        float timeout = Time.realtimeSinceStartup + TimeoutSeconds;

        while (!condition.Invoke() && Time.realtimeSinceStartup < timeout)
        {
            perception.Tick(Time.deltaTime, state);
            yield return null;
        }

        Assert.That(condition.Invoke(), Is.True, failureMessage);
    }

    private static Transform CreateHidingAnchor(
        Transform parent,
        string anchorName,
        Vector3 localPosition)
    {
        GameObject anchor = new(anchorName);
        anchor.transform.SetParent(parent, false);
        anchor.transform.localPosition = localPosition;
        return anchor.transform;
    }

    private static IEnumerator TickInvestigationUntil(
        EnemyInvestigateState state,
        Func<bool> condition,
        string failureMessage)
    {
        float timeout = Time.realtimeSinceStartup + TimeoutSeconds;

        while (!condition.Invoke() && Time.realtimeSinceStartup < timeout)
        {
            state.Tick(Time.deltaTime);
            yield return null;
        }

        Assert.That(condition.Invoke(), Is.True, failureMessage);
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
        EnemyNavigationConfig navigation =
            Track(UnityEngine.Object.Instantiate(source.NavigationProfile));
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
            "navigationProfile",
            navigation);
        PlayModeTestReflection.SetField(
            clone,
            "postureProfile",
            posture);
        return clone;
    }

    // Vision refreshes every quarter second, so there is always a frame with
    // no stimulus in it. A state that is not counted as working a target
    // loses that target on such a frame, and an enemy standing and watching a
    // player forgets it and wanders back to patrol.
    [UnityTest]
    public IEnumerator Stalking_KeepsItsTargetBetweenVisionRefreshes()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        GameplayNoiseWorldService noiseWorld = CreateNoiseWorld();

        // Far enough out to be stalked rather than chased.
        EnemyTarget target = CreateSpawnedTarget(
            new Vector3(0f, 0f, 6f),
            canBeDetected: true);

        NetworkEnemyController enemy = CreateSpawnedProductionEnemy(
            enemyPrefab,
            noiseWorld,
            new Vector3(0f, 0f, -6f));
        EnemyServerRuntime runtime = enemy.GetComponent<EnemyServerRuntime>();

        yield return WaitForCondition(
            () => runtime.IsRunning,
            "Enemy did not start.");

        yield return WaitForCondition(
            () => enemy.CurrentState == EnemyState.Stalk,
            "Enemy did not settle into stalking a distant target.");

        // Standing with the target visible never reaches the branch this is
        // about - there is a stimulus every refresh. Taking the target out of
        // sight for a moment is what a player stepping behind something does,
        // and it is the frame on which an unengaged state throws its target
        // away.
        PlayModeTestReflection.SetField(target, "canBeDetected", false);

        yield return new WaitForSecondsRealtime(0.6f);

        // Dropping the target and going to look is correct - chasing does the
        // same, and demanding otherwise would be asking stalking to be more
        // stubborn than the behaviour it replaces. What must not happen is
        // going straight back to patrol: that is what a state outside the
        // engaged set does, throwing the target and the search memory away
        // together and leaving nothing to look for.
        Assert.That(
            enemy.CurrentState,
            Is.Not.EqualTo(EnemyState.Patrol),
            "The enemy went back to patrolling the moment the target stepped " +
            "out of sight, instead of going to look for it.");
    }

    // The sequence the whole thing was built for, end to end. Nothing before
    // this reached past stalking: the other fixtures use a target with no
    // gaze, and without something that can look back there is no being
    // noticed, so no retreat, no going round and no ambush.
    [UnityTest]
    public IEnumerator StalkChain_GoesRoundBehindAndLiesInWait()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeLargeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        GameplayNoiseWorldService noiseWorld = CreateNoiseWorld();

        EnemyTarget target = CreateSpawnedTarget(
            new Vector3(0f, 0f, 6f),
            canBeDetected: true,
            withGaze: true);
        PlayerGazeNetwork gaze = target.GetComponent<PlayerGazeNetwork>();

        NetworkEnemyController enemy = CreateSpawnedProductionEnemy(
            enemyPrefab,
            noiseWorld,
            new Vector3(0f, 0f, -6f));
        EnemyServerRuntime runtime = enemy.GetComponent<EnemyServerRuntime>();

        yield return WaitForCondition(
            () => runtime.IsRunning,
            "Enemy did not start.");

        // Looking away, so being spotted is not what brings on the stalk.
        target.transform.rotation = Quaternion.Euler(0f, 0f, 0f);

        yield return WaitForCondition(
            () => enemy.CurrentState == EnemyState.Stalk,
            "Enemy did not stalk a target seen from a distance.");

        // Turn round and look straight at it.
        target.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        Physics.SyncTransforms();

        yield return WaitForCondition(
            () => enemy.CurrentState == EnemyState.Retreat,
            "Enemy did not break off after being looked at.");

        // Look away again so it can get round without being seen.
        target.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
        Physics.SyncTransforms();

        yield return WaitForCondition(
            20f,
            () => enemy.CurrentState == EnemyState.Ambush,
            "Enemy never got behind the target and settled into an ambush.");

        Assert.That(
            gaze.CanSeeBody(enemy.transform.position, 1.8f),
            Is.False,
            "The ambush position is in the target's view.");

        // Turning round on something standing behind you is the beat the whole
        // sequence exists to deliver. Open the gap on the same frame as well:
        // an Ambush used to hand over to an ordinary Chase, whose next distance
        // refresh immediately chose Stalk again here.
        Vector3 escapeDirection = target.transform.position -
                                  enemy.transform.position;
        escapeDirection.y = 0f;
        escapeDirection.Normalize();

        Vector3 escapedPosition = enemy.transform.position +
                                  escapeDirection * 16f;

        target.transform.position = escapedPosition;
        target.transform.rotation = Quaternion.LookRotation(
            enemy.transform.position - escapedPosition,
            Vector3.up
        );
        Physics.SyncTransforms();

        Assert.That(
            Vector3.Distance(
                enemy.transform.position,
                target.transform.position),
            Is.GreaterThan(enemyConfig.stalkInsteadOfChasingDistance),
            "The target did not open enough distance to exercise the " +
            "Chase-to-Stalk fork."
        );

        yield return WaitForCondition(
            () => enemy.CurrentState == EnemyState.Chase ||
                  enemy.CurrentState == EnemyState.Attack,
            "The target turned round onto the ambush and the enemy did not " +
            "come at it.");

        float assaultDeadline = Time.realtimeSinceStartup + 1f;

        while (Time.realtimeSinceStartup < assaultDeadline)
        {
            Assert.That(
                EnemyStateRules.IsStealthManeuver(enemy.CurrentState),
                Is.False,
                "A triggered ambush restarted the stealth manoeuvre when the " +
                "target opened the gap."
            );

            yield return null;
        }
    }

    // Backing away is measured against the target, not against the compass.
    // The retreat fan reaches a hundred and thirty five degrees off straight
    // back, which at this state's distances swings round the target and
    // finishes on the far side of it - further away than it started, having
    // walked through the gap on the way. In a dead end that is the only
    // candidate the level offers, so it was taken, and the enemy walked at
    // someone who was looking straight at it.
    [UnityTest]
    public IEnumerator Retreat_DoesNotCloseOnATargetThatIsWatching()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeDeadEndFacingTheTarget(standingAgentTypeId, crawlingAgentTypeId);

        GameplayNoiseWorldService noiseWorld = CreateNoiseWorld();

        EnemyTarget target = CreateSpawnedTarget(
            new Vector3(0f, 0f, 3f),
            canBeDetected: true,
            withGaze: true);

        NetworkEnemyController enemy = CreateSpawnedProductionEnemy(
            enemyPrefab,
            noiseWorld,
            new Vector3(0f, 0f, -9f));
        EnemyServerRuntime runtime = enemy.GetComponent<EnemyServerRuntime>();

        yield return WaitForCondition(
            () => runtime.IsRunning,
            "Enemy did not start.");

        // Looking away, so the stalk is what being seen from a distance
        // brings on rather than being caught.
        target.transform.rotation = Quaternion.Euler(0f, 0f, 0f);

        yield return WaitForCondition(
            () => enemy.CurrentState == EnemyState.Stalk,
            "Enemy did not stalk a target seen from a distance.");

        target.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        Physics.SyncTransforms();

        yield return WaitForCondition(
            () => enemy.CurrentState == EnemyState.Retreat,
            "Enemy did not break off after being looked at.");

        float startDistance = Vector3.Distance(
            enemy.transform.position,
            target.transform.position);
        float closest = startDistance;
        float deadline = Time.realtimeSinceStartup + 5f;

        while (Time.realtimeSinceStartup < deadline &&
               enemy.CurrentState == EnemyState.Retreat)
        {
            closest = Mathf.Min(
                closest,
                Vector3.Distance(
                    enemy.transform.position,
                    target.transform.position));

            yield return null;
        }

        // Standing still is a fine retreat answer here - there is nowhere to
        // back away to. Walking in before admitting that stealth has failed is
        // not.
        Assert.That(
            closest,
            Is.GreaterThan(startDistance - 1f),
            $"The enemy closed while retreating from a target watching it, from " +
            $"{startDistance:F2} to {closest:F2}.");

        Assert.That(
            enemy.CurrentState,
            Is.EqualTo(EnemyState.Chase).Or.EqualTo(EnemyState.Attack),
            "A retreat with no safe escape did not escalate to assault.");
    }

    // Being watched used to be one bool for the whole server, so a retreat was
    // planned against the one pose the enemy had observed and nobody else.
    // Here a second player stands behind the enemy in the stem, watching it,
    // and is not a target the enemy could pick up - so the only thing that can
    // keep the enemy off them is the retreat accounting for every watcher
    // rather than for its own victim.
    //
    // Backing straight away from the target is backing into that second
    // player, and the stem is too narrow to step aside in. Standing still is
    // the only honest answer the level offers.
    [UnityTest]
    public IEnumerator Retreat_DoesNotBackAwayIntoASecondPlayerWatching()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeDeadEndFacingTheTarget(standingAgentTypeId, crawlingAgentTypeId);

        GameplayNoiseWorldService noiseWorld = CreateNoiseWorld();

        EnemyTarget target = CreateSpawnedTarget(
            new Vector3(0f, 0f, 5f),
            canBeDetected: true,
            withGaze: true);

        // Watches, cannot be watched for. Detectable, it would simply be the
        // nearer target and the enemy would deal with it instead, which is a
        // different behaviour from the one under test.
        EnemyTarget watcher = CreateSpawnedTarget(
            new Vector3(0f, 0f, -10f),
            canBeDetected: false,
            withGaze: true);

        // Both looking down the stem at the enemy between them.
        target.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        watcher.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
        Physics.SyncTransforms();

        NetworkEnemyController enemy = CreateSpawnedProductionEnemy(
            enemyPrefab,
            noiseWorld,
            new Vector3(0f, 0f, -5f));
        EnemyServerRuntime runtime = enemy.GetComponent<EnemyServerRuntime>();

        yield return WaitForCondition(
            () => runtime.IsRunning,
            "Enemy did not start.");

        yield return WaitForCondition(
            () => enemy.CurrentState == EnemyState.Retreat,
            "Enemy did not break off while two players watched it.");

        float startToTarget = Vector3.Distance(
            enemy.transform.position,
            target.transform.position);
        float startToWatcher = Vector3.Distance(
            enemy.transform.position,
            watcher.transform.position);
        float closestToTarget = startToTarget;
        float closestToWatcher = startToWatcher;
        float deadline = Time.realtimeSinceStartup + 5f;

        while (Time.realtimeSinceStartup < deadline &&
               enemy.CurrentState == EnemyState.Retreat)
        {
            closestToTarget = Mathf.Min(
                closestToTarget,
                Vector3.Distance(
                    enemy.transform.position,
                    target.transform.position));
            closestToWatcher = Mathf.Min(
                closestToWatcher,
                Vector3.Distance(
                    enemy.transform.position,
                    watcher.transform.position));

            yield return null;
        }

        Assert.That(
            closestToWatcher,
            Is.GreaterThan(startToWatcher - 1.5f),
            "While retreating, the enemy backed away from its target straight into the second " +
            $"player watching it, from {startToWatcher:F2} to " +
            $"{closestToWatcher:F2}.");

        Assert.That(
            closestToTarget,
            Is.GreaterThan(startToTarget - 1.5f),
            $"While retreating, the enemy closed on the target it was breaking off from, from " +
            $"{startToTarget:F2} to {closestToTarget:F2}.");

        Assert.That(
            enemy.CurrentState,
            Is.EqualTo(EnemyState.Chase).Or.EqualTo(EnemyState.Attack),
            "A retreat blocked by two watchers did not escalate to assault.");
    }

    // The manoeuvre's target leaving the level, at each phase of it, with
    // somebody else standing in plain sight.
    //
    // Perception nulls a target the moment it stops being valid, and the
    // selector's "nothing to defend, take anything visible" branch used to be
    // checked before the lock was - so a despawn handed the approach, and the
    // ambush at the end of it, to whichever other player was in view, running
    // a plan built around somebody who had left. The phases carry on against
    // the pose they observed, which is the point of them, but never against a
    // different person: only leaving the manoeuvre releases the lock.
    [UnityTest]
    public IEnumerator StalkDespawn_DoesNotHandTheApproachToAnotherPlayer()
    {
        yield return DespawnDuringPhase(EnemyState.Stalk);
    }

    [UnityTest]
    public IEnumerator RetreatDespawn_DoesNotHandTheApproachToAnotherPlayer()
    {
        yield return DespawnDuringPhase(EnemyState.Retreat);
    }

    [UnityTest]
    public IEnumerator FlankDespawn_DoesNotHandTheApproachToAnotherPlayer()
    {
        yield return DespawnDuringPhase(EnemyState.Flank);
    }

    [UnityTest]
    public IEnumerator AmbushDespawn_DoesNotHandTheApproachToAnotherPlayer()
    {
        yield return DespawnDuringPhase(EnemyState.Ambush);
    }

    private IEnumerator DespawnDuringPhase(EnemyState phase)
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        GameplayNoiseWorldService noiseWorld = CreateNoiseWorld();

        EnemyTarget target = CreateSpawnedTarget(
            new Vector3(0f, 0f, 6f),
            canBeDetected: true,
            withGaze: true);

        // Standing beside the target, in the open, and switched on only when
        // the target goes - so up to that moment there is exactly one person
        // the enemy could be dealing with, and from it there is exactly one
        // person left to be tempted by. Beside the target rather than beside
        // the enemy on purpose: whatever the manoeuvre ends in walks back
        // towards where the target was last, so this one is certain to be
        // found once the lock is released.
        EnemyTarget challenger = CreateSpawnedTarget(
            new Vector3(4f, 0f, 4f),
            canBeDetected: false);
        ulong challengerObjectId =
            challenger.GetComponent<NetworkObject>().NetworkObjectId;

        NetworkEnemyController enemy = CreateSpawnedProductionEnemy(
            enemyPrefab,
            noiseWorld,
            new Vector3(0f, 0f, -6f));
        EnemyServerRuntime runtime = enemy.GetComponent<EnemyServerRuntime>();

        yield return WaitForCondition(
            () => runtime.IsRunning,
            "Enemy did not start.");

        yield return DriveManeuverTo(phase, enemy, target);

        Assert.That(
            enemy.CurrentState,
            Is.EqualTo(phase),
            "The manoeuvre did not settle on the phase being tested.");

        PlayModeTestReflection.SetField(challenger, "canBeDetected", true);
        target.GetComponent<NetworkObject>().Despawn(destroy: true);

        // Two halves, and the second is what stops the first proving nothing.
        // While the manoeuvre runs, the person standing in plain sight must
        // not become its target. Once it ends - a phase timing out, the
        // overall deadline, or a search starting - the lock is released and
        // that same person, in that same spot, is picked up. Refused because
        // it was locked, not because it could not be seen.
        bool leftTheManeuver = false;
        bool tookTheChallenger = false;
        float deadline = Time.realtimeSinceStartup + 24f;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (!leftTheManeuver)
            {
                if (EnemyStateRules.IsStealthManeuver(enemy.CurrentState))
                {
                    Assert.That(
                        TargetsNetworkObject(enemy, challengerObjectId),
                        Is.False,
                        $"The enemy carried its {phase} on against a " +
                        "different player once its own target despawned.");
                }
                else
                {
                    leftTheManeuver = true;
                }
            }
            else if (TargetsNetworkObject(enemy, challengerObjectId))
            {
                tookTheChallenger = true;
                break;
            }

            yield return null;
        }

        Assert.That(
            leftTheManeuver,
            Is.True,
            $"The {phase} never ended after its target left the level.");

        Assert.That(
            tookTheChallenger,
            Is.True,
            "The enemy never picked up the player standing in plain sight, " +
            "so the refusal above proved nothing.");
    }

    // Stalk needs the target seen from a distance; the rest need it looking at
    // the enemy and then away again, which is the sequence StalkChain walks.
    private IEnumerator DriveManeuverTo(
        EnemyState phase,
        NetworkEnemyController enemy,
        EnemyTarget target)
    {
        target.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
        Physics.SyncTransforms();

        yield return WaitForCondition(
            () => enemy.CurrentState == EnemyState.Stalk,
            "Enemy did not stalk a target seen from a distance.");

        if (phase == EnemyState.Stalk)
        {
            yield break;
        }

        target.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        Physics.SyncTransforms();

        yield return WaitForCondition(
            () => enemy.CurrentState == EnemyState.Retreat,
            "Enemy did not break off after being looked at.");

        if (phase == EnemyState.Retreat)
        {
            yield break;
        }

        target.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
        Physics.SyncTransforms();

        if (phase == EnemyState.Flank)
        {
            yield return WaitForCondition(
                () => enemy.CurrentState == EnemyState.Flank,
                "Enemy did not go round after breaking sight.");

            yield break;
        }

        yield return WaitForCondition(
            20f,
            () => enemy.CurrentState == EnemyState.Ambush,
            "Enemy never got behind the target and settled into an ambush.");
    }

    private static bool TargetsNetworkObject(
        NetworkEnemyController enemy,
        ulong networkObjectId)
    {
        EnemyTargetIdentity identity = enemy.CurrentTargetIdentity;

        return identity.HasTarget &&
               identity.TargetObject.NetworkObjectId == networkObjectId;
    }

    // The search route reaches four metres from the origin at its furthest,
    // so a six metre room holds the branch points and not the leaves. A room
    // big enough for all of them would make the filter untestable.
    [UnityTest]
    public IEnumerator SearchPlan_StaysInTheRoomTheStimulusCameFrom()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        RoomVolume room = CreateSearchRoom(
            new Vector3(0f, 1.5f, 0f),
            new Vector3(6f, 5f, 6f));

        yield return null;

        EnemyInvestigationSearchPlanner planner = new();
        planner.BuildHierarchicalSearchPlan(
            Vector3.zero,
            new Vector3(0f, 0f, -8f),
            branchRadius: 2.5f,
            branchPointCount: 3,
            leafRadius: 1.5f,
            leafPointCountPerBranch: 3,
            StandingQueryFilter(standingAgentTypeId));

        Assert.That(
            planner.PointCount,
            Is.GreaterThan(0),
            "The room swallowed the whole route.");

        for (int i = 0; i < planner.PointCount; i++)
        {
            Assert.That(planner.TryGetPoint(i, out Vector3 point), Is.True);
            Assert.That(
                room.Contains(point),
                Is.True,
                $"Search point {i} at {point} is outside the room the noise " +
                "came from.");
        }
    }

    // Room volumes are dragged into place by eye and their undersides sit
    // above the floor as often as not, while search points land on the
    // NavMesh. Judged strictly, such a room rejects everything, the plan
    // empties, the fallback drops the room, and the search spills all over
    // the level - which reads as the filter doing nothing at all.
    [UnityTest]
    public IEnumerator SearchPlan_StaysInARoomWhoseVolumeFloatsAboveTheFloor()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        // Underside at 0.3, well clear of the floor the points land on.
        RoomVolume room = CreateSearchRoom(
            new Vector3(0f, 1.8f, 0f),
            new Vector3(6f, 3f, 6f));

        yield return null;

        Assert.That(
            room.Bounds.min.y,
            Is.GreaterThan(0.2f),
            "The fixture no longer floats the room above the floor.");

        EnemyInvestigationSearchPlanner planner = new();
        planner.BuildHierarchicalSearchPlan(
            Vector3.zero,
            new Vector3(0f, 0f, -8f),
            branchRadius: 2.5f,
            branchPointCount: 3,
            leafRadius: 1.5f,
            leafPointCountPerBranch: 3,
            StandingQueryFilter(standingAgentTypeId));

        Assert.That(planner.PointCount, Is.GreaterThan(0));

        for (int i = 0; i < planner.PointCount; i++)
        {
            planner.TryGetPoint(i, out Vector3 point);

            Assert.That(
                Mathf.Max(Mathf.Abs(point.x), Mathf.Abs(point.z)),
                Is.LessThanOrEqualTo(3.05f),
                $"Search point {i} at {point} escaped the room's footprint.");
        }
    }

    // Most of the level has no rooms marked up, and the search has to carry
    // on exactly as it did before it knew what a room was.
    [UnityTest]
    public IEnumerator SearchPlan_WithoutRoomsReachesAsFarAsItAlwaysDid()
    {
        yield return StartHost();

        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeOpenArena(standingAgentTypeId, crawlingAgentTypeId);

        yield return null;

        EnemyInvestigationSearchPlanner planner = new();
        planner.BuildHierarchicalSearchPlan(
            Vector3.zero,
            new Vector3(0f, 0f, -8f),
            branchRadius: 2.5f,
            branchPointCount: 3,
            leafRadius: 1.5f,
            leafPointCountPerBranch: 3,
            StandingQueryFilter(standingAgentTypeId));

        float furthest = 0f;

        for (int i = 0; i < planner.PointCount; i++)
        {
            planner.TryGetPoint(i, out Vector3 point);
            furthest = Mathf.Max(
                furthest,
                Vector3.ProjectOnPlane(point, Vector3.up).magnitude);
        }

        Assert.That(
            furthest,
            Is.GreaterThan(3f),
            "With no room to stay inside, the route should still spread past " +
            "the branch ring.");
    }

    // The arena is baked for the production agent types, so sampling with the
    // project's first agent type - which is what the filterless overload
    // uses - finds no NavMesh at all.
    private static NavMeshQueryFilter StandingQueryFilter(int standingAgentTypeId)
    {
        return new NavMeshQueryFilter
        {
            agentTypeID = standingAgentTypeId,
            areaMask = NavMesh.AllAreas
        };
    }

    private RoomVolume CreateSearchRoom(Vector3 centre, Vector3 size)
    {
        GameObject root = Track(new GameObject("Search room"));
        root.SetActive(false);

        GameObject part = new("Part");
        part.transform.SetParent(root.transform, false);
        part.transform.position = centre;

        BoxCollider partCollider = part.AddComponent<BoxCollider>();
        partCollider.size = size;
        partCollider.isTrigger = true;

        RoomVolume room = root.AddComponent<RoomVolume>();
        root.SetActive(true);

        return room;
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

    private void BakeLargeOpenArena(
        int standingAgentTypeId,
        int crawlingAgentTypeId)
    {
        GameObject root = Track(new GameObject("Large prebuilt NavMesh arena"));
        CreateGeometry(
            "Large arena floor",
            new Vector3(0f, -0.1f, 0f),
            new Vector3(40f, 0.2f, 40f),
            root.transform);
        BakeSurfaces(
            root,
            standingAgentTypeId,
            crawlingAgentTypeId);
    }

    private void BakePushCorridor(
        int standingAgentTypeId,
        int crawlingAgentTypeId)
    {
        GameObject root = Track(new GameObject("Prebuilt push corridor"));
        CreateGeometry(
            "Push corridor floor",
            new Vector3(0f, -0.1f, 0f),
            new Vector3(4f, 0.2f, 14f),
            root.transform);
        CreateGeometry(
            "Push corridor left wall",
            new Vector3(-1.9f, 1f, 0f),
            new Vector3(0.2f, 2f, 14f),
            root.transform);
        CreateGeometry(
            "Push corridor right wall",
            new Vector3(1.9f, 1f, 0f),
            new Vector3(0.2f, 2f, 14f),
            root.transform);
        BakeSurfaces(
            root,
            standingAgentTypeId,
            crawlingAgentTypeId);
    }

    // One room, one doorway at x 1..5, reachable only by turning: a straight
    // line from the enemy to a target inside the room misses the doorway.
    private void BakeBarricadedRoom(
        int standingAgentTypeId,
        int crawlingAgentTypeId)
    {
        GameObject root = Track(new GameObject("Prebuilt barricaded room"));
        CreateGeometry(
            "Room floor",
            new Vector3(0f, -0.1f, 0f),
            new Vector3(20f, 0.2f, 20f),
            root.transform);
        CreateGeometry(
            "Room wall left of doorway",
            new Vector3(-4.5f, 1f, 0f),
            new Vector3(11f, 2f, 0.4f),
            root.transform);
        CreateGeometry(
            "Room wall right of doorway",
            new Vector3(7.5f, 1f, 0f),
            new Vector3(5f, 2f, 0.4f),
            root.transform);
        BakeSurfaces(
            root,
            standingAgentTypeId,
            crawlingAgentTypeId);
    }

    // A dead end whose only opening faces the target: a stem the enemy stands
    // in, and a room beyond it that the target stands in. Everything sideways
    // and behind runs off the mesh, so the only reachable retreat candidates
    // are the wide ones that swing round toward the target.
    private void BakeDeadEndFacingTheTarget(
        int standingAgentTypeId,
        int crawlingAgentTypeId)
    {
        GameObject root = Track(new GameObject("Prebuilt dead end"));
        CreateGeometry(
            "Dead end room",
            new Vector3(0f, -0.1f, 2.5f),
            new Vector3(16f, 0.2f, 11f),
            root.transform);
        CreateGeometry(
            "Dead end stem",
            new Vector3(0f, -0.1f, -7f),
            new Vector3(2f, 0.2f, 8f),
            root.transform);
        BakeSurfaces(
            root,
            standingAgentTypeId,
            crawlingAgentTypeId);
    }

    private void BakeLCorridor(
        int standingAgentTypeId,
        int crawlingAgentTypeId)
    {
        GameObject root = Track(new GameObject("Prebuilt L corridor"));
        CreateGeometry(
            "L corridor first leg",
            new Vector3(0f, -0.1f, 2.5f),
            new Vector3(2f, 0.2f, 7f),
            root.transform);
        CreateGeometry(
            "L corridor second leg",
            new Vector3(2.5f, -0.1f, 5f),
            new Vector3(7f, 0.2f, 2f),
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

    // The dual-NavMesh corridor with a posture-capable enemy standing on it.
    // Shared by the traversal test and the tactical planning test, which need
    // the same setup and have nothing else in common.
    private EnemyNavigator BuildPostureEnemyOnLowCeilingCorridor(
        Vector3 position,
        out EnemyConfig config,
        out EnemyPostureController postureController,
        out NavMeshAgent agent)
    {
        GetProductionAgentTypes(
            enemyPrefab,
            out int standingAgentTypeId,
            out int crawlingAgentTypeId);
        BakeLowCeilingCorridor(
            standingAgentTypeId,
            crawlingAgentTypeId);

        config = CloneNavigationConfig(enemyConfig);
        EnemyPostureConfig postureProfile = config.PostureProfile;
        postureProfile.standingToCrawlingTransitionDuration = 0f;
        postureProfile.crawlingToStandingTransitionDuration = 0f;
        postureProfile.minPostureDuration = 0f;
        postureProfile.standingRecoveryCheckInterval = 0.05f;

        GameObject actor = Track(new GameObject("Posture navigation enemy"));
        actor.SetActive(false);
        actor.layer = 6;
        actor.transform.position = position;
        actor.AddComponent<NetworkObject>();

        agent = actor.AddComponent<NavMeshAgent>();
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
        postureController = actor.AddComponent<EnemyPostureController>();
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
            TryPlaceAgent(agent, position),
            Is.True,
            "Posture enemy could not be placed on the standing NavMesh.");
        Assert.That(
            postureController.TryInitializeServer(config, networkState),
            Is.True);
        navigator.Configure(config);

        return navigator;
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

    private static float CalculatePlanLength(
        Vector3 start,
        IReadOnlyList<Vector3> plan,
        NavMeshQueryFilter filter)
    {
        float length = 0f;
        Vector3 segmentStart = start;

        for (int i = 0; i < plan.Count; i++)
        {
            length += CalculatePathLength(segmentStart, plan[i], filter);
            segmentStart = plan[i];
        }

        return length;
    }

    private static float CalculatePathLength(
        Vector3 start,
        Vector3 destination,
        NavMeshQueryFilter filter)
    {
        NavMeshPath path = new();

        Assert.That(
            NavMesh.CalculatePath(start, destination, filter, path),
            Is.True);
        Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));

        float length = 0f;
        Vector3[] corners = path.corners;

        for (int i = 1; i < corners.Length; i++)
        {
            length += Vector3.Distance(corners[i - 1], corners[i]);
        }

        return length;
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

    private static IEnumerator WaitForCondition(
        Func<bool> condition,
        string failureMessage)
    {
        yield return WaitForCondition(
            TimeoutSeconds,
            condition,
            failureMessage);
    }

    private static IEnumerator WaitForCondition(
        float timeoutSeconds,
        Func<bool> condition,
        string failureMessage)
    {
        float timeout = Time.realtimeSinceStartup + timeoutSeconds;

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
