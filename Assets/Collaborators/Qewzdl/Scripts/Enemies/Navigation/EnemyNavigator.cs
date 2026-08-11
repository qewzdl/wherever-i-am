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

    private const float MinimumFacingSqrMagnitude = 0.0001f;

    private Rigidbody body;
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
    private bool hasDeferredRepath;
    private float forcefulPushStopUntil = -1f;

    private readonly HashSet<ItemNavigationObstacle> pushThroughHolds = new();
    private readonly List<ItemNavigationObstacle> pushThroughCandidates = new();

    public Vector3 Position => transform.position;

    // How tall this enemy is, for anyone asking whether it can be seen. Taken
    // from the agent rather than written down again per state: it is already
    // the authority on the enemy's size, and it shrinks when the posture does.
    public float BodyHeight => agent != null ? agent.height : DefaultBodyHeight;
    public int NavigationSchedulerId => GetInstanceID();

    private const float DefaultBodyHeight = 1.8f;
    public EnemyNavigationQueryTelemetrySnapshot QueryTelemetry =>
        queryTelemetry != null
            ? queryTelemetry.Snapshot
            : default;

    public bool IsDirectApproachBlockedByItem(Vector3 destination)
    {
        return itemPusher != null &&
               itemPusher.IsDirectApproachBlockedByItem(destination);
    }

    // Turns the body toward a direction. Only safe to call at a standstill, and
    // only because nothing else writes rotation there: EnemyPhysicsMotor bails
    // out before rotating when it has no facing direction, and NavMeshAgent's
    // own updateRotation needs a velocity it does not have. While moving, both
    // of those own the rotation and this would fight them.
    public void FaceDirection(
        Vector3 direction,
        float degreesPerSecond,
        float deltaTime
    )
    {
        Vector3 flatDirection = new(direction.x, 0f, direction.z);

        if (flatDirection.sqrMagnitude < MinimumFacingSqrMagnitude)
        {
            return;
        }

        Quaternion target = Quaternion.LookRotation(flatDirection, Vector3.up);
        Quaternion next = Quaternion.RotateTowards(
            transform.rotation,
            target,
            Mathf.Max(0f, degreesPerSecond) * Mathf.Max(0f, deltaTime)
        );

        // Matches how EnemyPhysicsMotor writes rotation when it is driving, so
        // the two never disagree about who moved the body.
        if (body != null && !body.isKinematic)
        {
            body.MoveRotation(next);
            return;
        }

        transform.rotation = next;
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
        hasDeferredRepath = false;
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
        hasDeferredRepath = false;
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
        EnemyServerPerceptionScheduler.Forget(NavigationSchedulerId);
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
        if (!hasRequestedNavigation)
        {
            doorTraversal?.Cancel();
            return;
        }

        if (doorTraversal == null)
        {
            RetryDeferredRepath();
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

            return;
        }

        if (hasDeferredRepath)
        {
            RetryDeferredRepath();
        }
    }

    private void RetryDeferredRepath()
    {
        if (!hasDeferredRepath)
        {
            return;
        }

        TryMoveAfterDoorNavigation(
            requestedNavigationDestination,
            requestedNavigationSpeed,
            forceRepath: true
        );
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

                // Every halt above (door interaction, posture transition,
                // barricade shove) latches agent.isStopped, and only a
                // successful repath ever cleared it. Reusing a still-valid
                // path skips the repath, so an enemy that stopped for a door
                // stayed frozen until the destination moved far enough to
                // force one - a barricaded, standing-still player never did.
                agent.isStopped = false;
            }

            queryTelemetry.RecordDeferredRepath();
            queryTelemetry.RecordReusedPath();
            return true;
        }

        int requestedPathQueries = navigationConfig != null
            ? navigationConfig.maximumPathQueriesPerRepath
            : 24;

        if (!EnemyServerPerceptionScheduler.TryReservePathQueries(
                NavigationSchedulerId,
                requestedPathQueries,
                out int grantedPathQueries))
        {
            hasDeferredRepath = true;
            queryTelemetry.RecordDeferredRepath();
            ContinueCurrentPath(speed);
            return true;
        }

        hasDeferredRepath = false;
        repathScheduler.RecordAttempt(
            destination,
            navigationConfig,
            EnemyNavigationTopology.Revision);
        queryService.BeginRepath(grantedPathQueries);

        if (!requestedAllowPushThrough)
        {
            ReleaseAllPushThroughHolds();
        }

        if (config != null && config.crawlingEnabled && postureController != null)
        {
            bool moved = TryMoveWithPosturePriority(destination, speed);

            if (!moved && queryService.WasPathBudgetExhausted)
            {
                hasDeferredRepath = true;
                queryTelemetry.RecordDeferredRepath();
                ContinueCurrentPath(speed);
                return true;
            }

            if (!moved && !postureController.IsPostureTransitionInProgress)
            {
                StopAtBlockedRoute();
            }

            return moved;
        }

        bool movedWithCurrentPosture = TryMoveToWithCurrentPosture(
            destination,
            speed);

        if (!movedWithCurrentPosture && queryService.WasPathBudgetExhausted)
        {
            hasDeferredRepath = true;
            queryTelemetry.RecordDeferredRepath();
            ContinueCurrentPath(speed);
            return true;
        }

        if (!movedWithCurrentPosture)
        {
            StopAtBlockedRoute();
        }

        return movedWithCurrentPosture;
    }

    private void ContinueCurrentPath(float speed)
    {
        if (agent == null ||
            !agent.enabled ||
            !agent.isOnNavMesh ||
            !agent.hasPath ||
            agent.isPathStale ||
            agent.pathStatus == NavMeshPathStatus.PathInvalid)
        {
            return;
        }

        agent.speed = Mathf.Max(0f, speed);
        agent.isStopped = false;
        queryTelemetry.RecordReusedPath();
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
        hasDeferredRepath = false;
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
        hasDeferredRepath = false;
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

    // Where this agent could actually stand, and the route it would actually
    // walk to get there.
    //
    // The stealth states used to ask NavMesh.SamplePosition and
    // NavMesh.CalculatePath with NavMesh.AllAreas and whichever agent type
    // happened to be default. That is a different NavMesh from the one this
    // enemy moves on - wrong agent size, wrong area mask, no posture, none of
    // the item blockers - so a plan could be judged perfect and then be
    // unwalkable the moment it was handed back here to be driven.
    // Costs one path query per posture tried, and says so. The tactical
    // planners reserve their queries from the server scheduler before they
    // start searching, and this is the only place that knows a candidate can
    // cost two of them - one for the current posture, one for the crawl
    // fallback. Counted here, the reservation is what actually limits them
    // instead of being a number nobody subtracted from.
    internal bool TryPlanTacticalRoute(
        Vector3 from,
        Vector3 to,
        NavMeshPath path,
        ref int pathBudget,
        out bool budgetExhausted
    )
    {
        budgetExhausted = false;

        if (path == null)
        {
            return false;
        }

        if (pathBudget <= 0)
        {
            budgetExhausted = true;
            return false;
        }

        EnemyPosture posture = CurrentTacticalPosture;
        pathBudget--;

        if (TryPlanTacticalRouteForPosture(posture, from, to, path))
        {
            return true;
        }

        // Same fallback order the real move takes: standing first, then drop
        // to a crawl if the level only offers a crawl-sized way through.
        if (config == null ||
            !config.crawlingEnabled ||
            posture == EnemyPosture.Crawling)
        {
            return false;
        }

        // Out of budget with the crawl untried. Saying "no route" here would
        // strike out a candidate on evidence that was never gathered, so the
        // caller is told to come back for it rather than move past it.
        if (pathBudget <= 0)
        {
            budgetExhausted = true;
            return false;
        }

        pathBudget--;

        return TryPlanTacticalRouteForPosture(
            EnemyPosture.Crawling,
            from,
            to,
            path);
    }

    internal bool TrySampleTacticalPoint(
        Vector3 desiredPosition,
        float maximumDistance,
        out Vector3 sampledPosition)
    {
        sampledPosition = desiredPosition;

        if (!TryGetNavigationQueryFilter(
                CurrentTacticalPosture,
                out NavMeshQueryFilter filter))
        {
            return false;
        }

        if (!NavMesh.SamplePosition(
                desiredPosition,
                out NavMeshHit hit,
                Mathf.Max(0.1f, maximumDistance),
                filter))
        {
            return false;
        }

        sampledPosition = hit.position;
        return true;
    }

    private EnemyPosture CurrentTacticalPosture =>
        postureController != null
            ? postureController.CurrentPosture
            : EnemyPosture.Standing;

    private bool TryPlanTacticalRouteForPosture(
        EnemyPosture posture,
        Vector3 from,
        Vector3 to,
        NavMeshPath path)
    {
        path.ClearCorners();

        if (!TryGetNavigationQueryFilter(posture, out NavMeshQueryFilter filter))
        {
            return false;
        }

        float sampleRadius = GetNavigationSampleRadius();

        if (!NavMesh.SamplePosition(
                from,
                out NavMeshHit fromHit,
                sampleRadius,
                filter) ||
            !NavMesh.SamplePosition(
                to,
                out NavMeshHit toHit,
                sampleRadius,
                filter))
        {
            path.ClearCorners();
            return false;
        }

        return NavMesh.CalculatePath(
                   fromHit.position,
                   toHit.position,
                   filter,
                   path) &&
               path.status == NavMeshPathStatus.PathComplete &&
               !HasNavigationBlockerOnPath(path);
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

        if (queryService.WasPathBudgetExhausted)
        {
            return false;
        }

        if (!requestedAllowPushThrough)
        {
            return false;
        }

        // Same barricade fallback as the standing-only path below, just
        // re-asking the posture planner (which may route standing or
        // crawling) once the blocking items' carving has been requested off.
        RequestPushThroughOnBlockingItems();

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
    // the blocking items' obstacle carving so the normal path rebuild can
    // route straight through them, and let the enemy's existing rigidbody
    // physically shove them aside on the way. Falls back to a stopped tick
    // while carving catches up; the repath scheduler retries on its own.
    private bool TryPushThrough(Vector3 destination, float speed)
    {
        RequestPushThroughOnBlockingItems();

        return TryBuildCompletePath(destination, out _) &&
               queryService.TryApplyPath(agent, pathBuffer, speed);
    }

    // Reaching here means every item-respecting route already failed, so the
    // rule is "path as if the items weren't there": lift carving on all of
    // them, not just one guessed from a straight line to the target. A
    // barricade off the sight line (a doorway needing a turn) was never
    // found by that scan, which left the enemy standing still forever.
    private void RequestPushThroughOnBlockingItems()
    {
        // Snapshot: RequestPushThrough mutates the live registry.
        pushThroughCandidates.Clear();
        pushThroughCandidates.AddRange(ItemNavigationObstacle.BlockingInstances);

        foreach (ItemNavigationObstacle blockingItem in pushThroughCandidates)
        {
            if (blockingItem == null || !pushThroughHolds.Add(blockingItem))
            {
                continue;
            }

            blockingItem.RequestPushThrough();

            if (Time.time >= forcefulPushStopUntil)
            {
                TryRollForcefulPush(blockingItem);
            }
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

        if (body == null)
        {
            body = GetComponent<Rigidbody>();
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
