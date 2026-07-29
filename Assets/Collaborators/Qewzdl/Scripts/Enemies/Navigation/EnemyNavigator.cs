using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyNavigator : MonoBehaviour
{
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private EnemyPostureController postureController;
    [SerializeField] private EnemyDoorInteractor doorInteractor;
    [SerializeField] private EnemyItemPusher itemPusher;

    private NavMeshPath pathBuffer;
    private EnemyNavigationQueryTelemetry queryTelemetry;
    private EnemyNavigationQueryService queryService;
    private EnemyNavigationRepathScheduler repathScheduler;
    private EnemyNavigationRecoveryController recoveryController;
    private EnemyDoorTraversalHandler doorTraversal;
    private EnemyPostureTraversalPlanner postureTraversal;

    private EnemyConfig config;
    private bool warnedAboutMissingNavMesh;

    private bool hasRequestedNavigation;
    private Vector3 requestedNavigationDestination;
    private float requestedNavigationSpeed;
    private bool requestedAllowPushThrough;
    private float forcefulPushStopUntil = -1f;

    private readonly HashSet<ItemNavigationObstacle> pushThroughHolds = new();

    public Vector3 Position => transform.position;
    public EnemyNavigationQueryTelemetrySnapshot QueryTelemetry =>
        queryTelemetry != null
            ? queryTelemetry.Snapshot
            : default;

    public bool IsDirectApproachBlockedByItem(Vector3 destination)
    {
        return itemPusher != null &&
               itemPusher.IsDirectApproachBlockedByItem(destination);
    }

    private void Awake()
    {
        CacheComponents();

        queryTelemetry = new EnemyNavigationQueryTelemetry();
        queryService = new EnemyNavigationQueryService(queryTelemetry);
        repathScheduler = new EnemyNavigationRepathScheduler();
        recoveryController = new EnemyNavigationRecoveryController(
            repathScheduler.Invalidate,
            queryTelemetry.RecordStuckRecovery);
        doorTraversal = new EnemyDoorTraversalHandler(doorInteractor);
        postureTraversal = new EnemyPostureTraversalPlanner(
            transform,
            agent,
            postureController,
            queryService,
            HasNavigationBlockerOnPath);

        pathBuffer = new NavMeshPath();
    }

    public void Configure(EnemyConfig config)
    {
        this.config = config;

        CacheComponents();

        hasRequestedNavigation = false;
        repathScheduler?.Invalidate();
        recoveryController?.Configure(config);
        queryTelemetry?.Reset();
        doorTraversal?.Cancel();

        if (config == null)
        {
            return;
        }

        postureController?.Configure(config);
        postureTraversal?.Configure(config);

        if (agent == null)
        {
            return;
        }

        agent.speed = config.patrolSpeed;
        agent.acceleration = config.acceleration;
        agent.angularSpeed = config.angularSpeed;
        agent.stoppingDistance = config.stoppingDistance;
        agent.autoRepath = false;
        agent.avoidancePriority = 25 +
            Mathf.Abs(GetInstanceID() % 50);

        if (postureController != null)
        {
            postureController.TrySetServerPosture(EnemyPosture.Standing);
            postureTraversal?.NotifyPostureChanged(EnemyPosture.Standing);
        }
    }

    public void DisableAgent()
    {
        CacheComponents();

        hasRequestedNavigation = false;
        repathScheduler?.Invalidate();
        recoveryController?.Reset();
        doorTraversal?.Cancel();
        ReleaseAllPushThroughHolds();
        forcefulPushStopUntil = -1f;

        if (agent != null)
        {
            agent.enabled = false;
        }
    }

    private void OnDisable()
    {
        ReleaseAllPushThroughHolds();
    }

    public bool TryMoveTo(Vector3 destination, float speed, bool allowPushThrough = false)
    {
        RememberRequestedNavigation(destination, speed, allowPushThrough);

        if (Time.time < forcefulPushStopUntil)
        {
            StopForForcefulPush();
            return true;
        }

        if (TryResolveDoorNavigation(
                destination,
                GetActiveRouteCorners(),
                out EnemyDoorNavigationResult doorResult))
        {
            if (doorResult.ShouldStop)
            {
                StopForDoorInteraction();
                return true;
            }

            if (doorResult.HasOverrideDestination)
            {
                destination = doorResult.OverrideDestination;
            }
        }

        return TryMoveAfterDoorNavigation(destination, speed);
    }

    public void TickNavigationGate()
    {
        if (doorTraversal == null)
        {
            return;
        }

        if (!hasRequestedNavigation)
        {
            doorTraversal.Cancel();
            return;
        }

        bool hadActiveInteraction = doorTraversal.IsActive;
        Vector3 probeDestination = GetDoorNavigationProbeDestination();
        EnemyDoorNavigationResult doorResult = doorTraversal.Evaluate(
            transform.position,
            probeDestination,
            GetActiveRouteCorners()
        );

        if (doorResult.ShouldStop)
        {
            StopForDoorInteraction();
            return;
        }

        if (doorResult.HasOverrideDestination)
        {
            TryMoveAfterDoorNavigation(
                doorResult.OverrideDestination,
                requestedNavigationSpeed,
                forceRepath: true
            );

            return;
        }

        if (hadActiveInteraction)
        {
            TryMoveAfterDoorNavigation(
                requestedNavigationDestination,
                requestedNavigationSpeed,
                forceRepath: true
            );
        }
    }

    private bool TryMoveAfterDoorNavigation(
        Vector3 destination,
        float speed,
        bool forceRepath = false)
    {
        if (postureController != null && postureController.IsPostureTransitionInProgress)
        {
            StopForPostureTransition();
            return false;
        }

        forceRepath |= TryRecoverStalledNavigation();

        EnemyNavigationConfig navigationConfig = config != null
            ? config.NavigationProfile
            : null;

        if (!repathScheduler.ShouldRepath(
                destination,
                agent,
                navigationConfig,
                EnemyNavigationTopology.Revision,
                forceRepath))
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.speed = Mathf.Max(0f, speed);
            }

            queryTelemetry.RecordDeferredRepath();
            queryTelemetry.RecordReusedPath();
            return true;
        }

        repathScheduler.RecordAttempt(
            destination,
            navigationConfig,
            EnemyNavigationTopology.Revision);
        queryService.BeginRepath(
            navigationConfig != null
                ? navigationConfig.maximumPathQueriesPerRepath
                : 24);

        if (!requestedAllowPushThrough)
        {
            ReleaseAllPushThroughHolds();
        }

        if (config != null && config.crawlingEnabled && postureController != null)
        {
            bool moved = TryMoveWithPosturePriority(destination, speed);

            if (!moved && !postureController.IsPostureTransitionInProgress)
            {
                StopAtBlockedRoute();
            }

            return moved;
        }

        bool movedWithCurrentPosture = TryMoveToWithCurrentPosture(
            destination,
            speed);

        if (!movedWithCurrentPosture)
        {
            StopAtBlockedRoute();
        }

        return movedWithCurrentPosture;
    }

    private bool TryRecoverStalledNavigation()
    {
        if (recoveryController == null ||
            agent == null ||
            !agent.enabled ||
            !agent.isOnNavMesh ||
            agent.pathPending ||
            agent.isStopped ||
            !agent.hasPath ||
            agent.remainingDistance <=
            Mathf.Max(0.1f, agent.stoppingDistance + 0.05f))
        {
            recoveryController?.Reset();
            return false;
        }

        recoveryController.EnsureTracking(transform.position);

        if (!recoveryController.TryRecover(transform.position))
        {
            return false;
        }

        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        return true;
    }

    public void Stop()
    {
        hasRequestedNavigation = false;
        doorTraversal?.Cancel();
        repathScheduler?.Invalidate();
        recoveryController?.Reset();
        ReleaseAllPushThroughHolds();

        if (!TryEnsureOnNavMesh())
        {
            return;
        }

        agent.isStopped = true;
    }

    public void ResetPath()
    {
        hasRequestedNavigation = false;
        doorTraversal?.Cancel();
        repathScheduler?.Invalidate();
        recoveryController?.Reset();
        ReleaseAllPushThroughHolds();

        if (!TryEnsureOnNavMesh())
        {
            return;
        }

        agent.ResetPath();
    }

    public bool HasReached(float reachDistance)
    {
        if (doorTraversal != null && doorTraversal.IsActive)
        {
            return false;
        }

        if (!TryEnsureOnNavMesh())
        {
            return false;
        }

        return !agent.pathPending && agent.remainingDistance <= reachDistance;
    }

    public bool TryEnsureOnNavMesh()
    {
        CacheComponents();

        if (agent == null || !agent.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (!agent.enabled)
        {
            agent.enabled = true;
        }

        if (agent.isOnNavMesh)
        {
            warnedAboutMissingNavMesh = false;
            return true;
        }

        if (TrySamplePositionForCurrentAgent(transform.position, out NavMeshHit hit)
            && agent.Warp(hit.position))
        {
            warnedAboutMissingNavMesh = false;
            return true;
        }

        if (!warnedAboutMissingNavMesh)
        {
            Debug.LogWarning(
                $"{nameof(EnemyNavigator)} is waiting for its {nameof(NavMeshAgent)} to be placed on a NavMesh.",
                this
            );

            warnedAboutMissingNavMesh = true;
        }

        return false;
    }

    internal bool TryGetNavigationQueryFilter(
        EnemyPosture posture,
        out NavMeshQueryFilter filter
    )
    {
        if (!TryEnsureOnNavMesh())
        {
            filter = default;
            return false;
        }

        int agentTypeId = postureController != null
            ? postureController.GetAgentTypeIdForPosture(posture)
            : agent.agentTypeID;

        filter = new NavMeshQueryFilter
        {
            agentTypeID = agentTypeId,
            areaMask = agent.areaMask
        };

        return true;
    }

    private void RememberRequestedNavigation(
        Vector3 destination,
        float speed,
        bool allowPushThrough)
    {
        requestedNavigationDestination = destination;
        requestedNavigationSpeed = speed;
        requestedAllowPushThrough = allowPushThrough;
        hasRequestedNavigation = true;
    }

    private bool TryResolveDoorNavigation(
        Vector3 destination,
        System.Collections.Generic.IReadOnlyList<Vector3> routeCorners,
        out EnemyDoorNavigationResult doorResult
    )
    {
        doorResult = EnemyDoorNavigationResult.None;

        if (doorTraversal == null)
        {
            return false;
        }

        doorResult = doorTraversal.Evaluate(
            transform.position,
            destination,
            routeCorners);
        return doorResult.IsHandled;
    }

    private Vector3[] GetActiveRouteCorners()
    {
        if (agent == null ||
            !agent.enabled ||
            !agent.isOnNavMesh ||
            !agent.hasPath)
        {
            NavMeshPath posturePath = postureTraversal?.LastPlannedPath;

            if (config != null &&
                config.crawlingEnabled &&
                posturePath != null &&
                posturePath.status != NavMeshPathStatus.PathInvalid)
            {
                return posturePath.corners;
            }

            return pathBuffer != null &&
                   pathBuffer.status != NavMeshPathStatus.PathInvalid
                    ? pathBuffer.corners
                    : null;
        }

        return agent.path.corners;
    }

    private Vector3 GetDoorNavigationProbeDestination()
    {
        if (agent == null ||
            !agent.enabled ||
            !agent.isOnNavMesh ||
            agent.pathPending ||
            !agent.hasPath)
        {
            return requestedNavigationDestination;
        }

        Vector3 steeringTarget = agent.steeringTarget;
        Vector3 flatDelta = steeringTarget - transform.position;
        flatDelta.y = 0f;

        return flatDelta.sqrMagnitude > 0.0025f
            ? steeringTarget
            : requestedNavigationDestination;
    }

    private void StopForDoorInteraction()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        agent.isStopped = true;
    }

    private bool TryMoveWithPosturePriority(
        Vector3 destination,
        float speed)
    {
        if (postureTraversal == null)
        {
            return false;
        }

        if (postureTraversal.TryBuildPlan(destination, out EnemyPostureTraversalPlan plan) &&
            plan.IsComplete)
        {
            return ApplyPosturePlan(plan, speed);
        }

        if (!requestedAllowPushThrough)
        {
            return false;
        }

        // Same barricade fallback as the standing-only path below, just
        // re-asking the posture planner (which may route standing or
        // crawling) once the blocking item's carving has been requested off.
        RequestPushThroughOnBlockingItem(destination);

        return postureTraversal.TryBuildPlan(destination, out EnemyPostureTraversalPlan retryPlan) &&
               retryPlan.IsComplete &&
               ApplyPosturePlan(retryPlan, speed);
    }

    private bool ApplyPosturePlan(EnemyPostureTraversalPlan plan, float speed)
    {
        float postureSpeed = postureController.GetSpeedForPosture(
            speed,
            plan.Posture);

        EnemyPosture previousPosture = postureController.CurrentPosture;

        if (!postureController.TrySetServerPosture(plan.Posture))
        {
            return false;
        }

        if (postureController.IsPostureTransitionInProgress)
        {
            StopForPostureTransition();
            return false;
        }

        if (previousPosture != postureController.CurrentPosture)
        {
            postureTraversal.NotifyPostureChanged(
                postureController.CurrentPosture);
        }

        return queryService.TryApplyPath(
            agent,
            plan.Path,
            postureSpeed);
    }

    private void StopForPostureTransition()
    {
        if (!TryEnsureOnNavMesh())
        {
            return;
        }

        agent.isStopped = true;
    }

    private bool TryMoveToWithCurrentPosture(
        Vector3 destination,
        float speed)
    {
        if (!TryEnsureOnNavMesh())
        {
            return false;
        }

        if (TryBuildCompletePath(destination, out _))
        {
            return queryService.TryApplyPath(agent, pathBuffer, speed);
        }

        return requestedAllowPushThrough && TryPushThrough(destination, speed);
    }

    // ponytail: no candidate scoring, no direct-movement mode - just lift
    // the blocking item's obstacle carving so the normal path rebuild can
    // route straight through it, and let the enemy's existing rigidbody
    // physically shove it aside on the way. Falls back to a stopped tick
    // while carving catches up; the repath scheduler retries on its own.
    private bool TryPushThrough(Vector3 destination, float speed)
    {
        RequestPushThroughOnBlockingItem(destination);

        return TryBuildCompletePath(destination, out _) &&
               queryService.TryApplyPath(agent, pathBuffer, speed);
    }

    private void RequestPushThroughOnBlockingItem(Vector3 destination)
    {
        if (itemPusher != null &&
            itemPusher.TryGetBlockingItem(destination, out ItemNavigationObstacle blockingItem) &&
            pushThroughHolds.Add(blockingItem))
        {
            blockingItem.RequestPushThrough();
            TryRollForcefulPush(blockingItem);
        }
    }

    // Occasional alternative to just walking through the barricade: stop
    // dead and shove it out of the way instead.
    private void TryRollForcefulPush(ItemNavigationObstacle blockingItem)
    {
        float chance = config != null ? config.barricadeShoveChance : 0f;

        if (chance <= 0f)
        {
            return;
        }

        // The blocking item is found by scanning the full corridor toward
        // the (possibly distant) destination, not just the enemy's
        // immediate surroundings - without this, a chasing enemy could
        // shove a barricade from clear across the level.
        float distanceToItem = Vector3.Distance(
            transform.position,
            blockingItem.transform.position);

        if (distanceToItem > config.barricadeShoveReach || Random.value > chance)
        {
            return;
        }

        blockingItem.ApplyForcefulPush(transform.position, config.barricadeShoveForce);
        forcefulPushStopUntil = Time.time + config.barricadeShoveStopDuration;
        StopForForcefulPush();
    }

    private void StopForForcefulPush()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        agent.isStopped = true;
    }

    private void ReleaseAllPushThroughHolds()
    {
        if (pushThroughHolds.Count == 0)
        {
            return;
        }

        foreach (ItemNavigationObstacle held in pushThroughHolds)
        {
            if (held != null)
            {
                held.ReleasePushThrough();
            }
        }

        pushThroughHolds.Clear();
    }

    private void StopAtBlockedRoute()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        // A single failed rebuild against a live, moving destination
        // (chasing a player) isn't proof the route is actually gone —
        // NavMesh sampling near a shifting target routinely misses by a
        // hair for one cycle. Only actually halt when there's no path
        // left to coast on; otherwise keep following the last
        // known-good path and let the next repath cycle try again.
        // Resetting on every transient miss caused a full stop-and-go
        // every few seconds during chase.
        if (agent.hasPath &&
            !agent.isPathStale &&
            agent.pathStatus != NavMeshPathStatus.PathInvalid)
        {
            return;
        }

        agent.isStopped = true;
        agent.ResetPath();
    }

    private bool TryBuildCompletePath(Vector3 destination, out Vector3 sampledDestination)
    {
        sampledDestination = destination;

        if (agent == null)
        {
            return false;
        }

        pathBuffer ??= new NavMeshPath();
        NavMeshQueryFilter filter = new()
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };

        float sampleRadius = GetNavigationSampleRadius();

        if (!queryService.TryBuildPath(
                transform.position,
                destination,
                sampleRadius,
                sampleRadius,
                filter,
                pathBuffer,
                out sampledDestination))
        {
            return false;
        }

        return IsSampledDestinationAcceptable(
                   destination,
                   sampledDestination) &&
               pathBuffer.status == NavMeshPathStatus.PathComplete &&
               !HasNavigationBlockerOnPath(pathBuffer);
    }

    private bool IsSampledDestinationAcceptable(
        Vector3 requestedDestination,
        Vector3 sampledDestination)
    {
        Vector3 delta = sampledDestination - requestedDestination;
        delta.y = 0f;
        float tolerance = config != null
            ? Mathf.Max(
                config.navigationDestinationRepathDistance,
                agent != null ? agent.stoppingDistance : 0f)
            : 0.3f;
        return delta.sqrMagnitude <= tolerance * tolerance;
    }

    private bool TrySamplePositionForCurrentAgent(Vector3 sourcePosition, out NavMeshHit hit)
    {
        if (agent == null)
        {
            hit = default;
            return false;
        }

        NavMeshQueryFilter filter = new()
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };

        return queryService.TrySamplePosition(
            sourcePosition,
            GetNavigationSampleRadius(),
            filter,
            out hit);
    }

    private bool HasNavigationBlockerOnPath(NavMeshPath path)
    {
        return itemPusher != null &&
               path != null &&
               itemPusher.HasNavigationBlockerOnRoute(path.corners);
    }

    private float GetNavigationSampleRadius()
    {
        return config != null
            ? Mathf.Max(0.1f, config.navigationNavMeshSampleRadius)
            : 2f;
    }

    private void CacheComponents()
    {
        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
        }

        if (postureController == null)
        {
            postureController = GetComponent<EnemyPostureController>();
        }

        if (doorInteractor == null)
        {
            doorInteractor = GetComponent<EnemyDoorInteractor>();
        }

        if (itemPusher == null)
        {
            itemPusher = GetComponent<EnemyItemPusher>();
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        CacheComponents();
    }
#endif
}
