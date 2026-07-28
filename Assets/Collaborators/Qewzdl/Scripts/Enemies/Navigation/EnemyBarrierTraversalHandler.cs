using UnityEngine;
using UnityEngine.AI;

// No complete NavMesh route exists to the destination. If — and only if
// — a real, reachable ItemNavigationObstacle is registered somewhere in
// the level, this walks toward it normally by NavMesh and drops to
// direct physical movement for the final stretch, where EnemyItemPusher's
// per-tick physical push (FixedUpdate) pushes whatever is actually in
// front of it — one item or a whole stack of them. Every
// GetDirectPathCheckInterval seconds it retries a full NavMesh path and
// hands back to normal pathing the moment one opens up. When no such
// item exists, the route is simply unreachable (walls, an isolated
// island, ...) and this refuses to engage — the caller stops instead of
// walking the enemy into a wall.
internal sealed class EnemyBarrierTraversalHandler : IEnemyTraversalHandler
{
    private const float DirectMovementActivationDistance = 0.2f;

    private readonly Transform ownerTransform;
    private readonly NavMeshAgent agent;
    private readonly EnemyItemPusher itemPusher;
    private readonly EnemyNavigationQueryService queryService;
    private readonly EnemyNavigationRecoveryController recoveryController;
    private readonly NavMeshPath recoveryPath = new();
    private readonly NavMeshPath barrierRoutePath = new();

    private EnemyConfig config;
    private bool directMovementActive;
    private Vector3 directMovementDestination;
    // The real requested destination can sit on the far side of a wall
    // that has nothing to do with the barrier being pushed — walking
    // straight at it would ram that wall once the barrier stops being
    // in the way. Physical movement instead aims at whichever active
    // barrier is nearest the enemy right now, refreshed every tick;
    // directMovementDestination stays the true goal for HasReached and
    // for the periodic normal-path recovery check.
    private Vector3 directMovementAimPoint;
    private ItemNavigationObstacle currentBarrier;
    private float directMovementSpeed;
    private int directMovementAgentTypeId;
    private int directMovementAreaMask;
    private float nextPathCheckTime;

    // A barrier that just timed out stuck-recovery isn't actually
    // pushable in practice (wedged against something else, most often)
    // — without this, FindBestActiveBarrier immediately re-selects the
    // very same barrier next tick and the enemy presses into it forever
    // in a tight retry loop. Excluding it briefly gives another barrier
    // (or a genuine stop, if none exists) a chance instead.
    private ItemNavigationObstacle failedBarrier;
    private float failedBarrierCooldownUntil;

    // Set while walking the reachable part of the route by NavMesh,
    // toward wherever it currently ends. Repathing only re-fires when
    // the requested destination itself changes, so reaching that partial
    // endpoint needs its own per-frame check (below in Tick) rather than
    // waiting for the next repath to notice.
    private bool hasPendingPushEndpoint;
    private Vector3 pendingPushEndpoint;
    private Vector3 pendingPushDestination;
    private float pendingPushSpeed;

    public EnemyTraversalKind Kind => EnemyTraversalKind.PushableBarrier;
    public bool IsActive => directMovementActive;
    public bool IsDirectMovementActive => directMovementActive;

    public EnemyBarrierTraversalHandler(
        Transform ownerTransform,
        NavMeshAgent agent,
        EnemyItemPusher itemPusher,
        EnemyNavigationQueryService queryService,
        EnemyNavigationRecoveryController recoveryController)
    {
        this.ownerTransform = ownerTransform;
        this.agent = agent;
        this.itemPusher = itemPusher;
        this.queryService = queryService;
        this.recoveryController = recoveryController;
    }

    public void Configure(EnemyConfig enemyConfig)
    {
        config = enemyConfig;
        Cancel(restoreAgent: true);
    }

    public bool TryGetDirectMovementIntent(
        out Vector3 destination,
        out float speed)
    {
        destination = directMovementAimPoint;
        speed = directMovementSpeed;
        return directMovementActive;
    }

    public bool HasReached(float reachDistance)
    {
        if (!directMovementActive)
        {
            return false;
        }

        Vector3 delta = directMovementDestination - ownerTransform.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= reachDistance * reachDistance;
    }

    // Only ever pushes toward a confirmed, reachable ItemNavigationObstacle
    // — never just because a complete path couldn't be found. A route can
    // be incomplete for reasons that have nothing to do with a pushable
    // item (permanent walls, an unreachable target island, geometry the
    // NavMesh was never baked to cross), and ramming those is not a
    // traversal strategy. Walks the barrier route normally by NavMesh so
    // the enemy still steers around walls instead of cutting a straight
    // line, and only drops to direct physical movement once at its end,
    // where EnemyItemPusher takes over pushing.
    public bool TryPushThroughToTarget(Vector3 destination, float speed)
    {
        if (agent == null)
        {
            return false;
        }

        if (directMovementActive)
        {
            directMovementDestination = destination;
            directMovementAimPoint = TryGetNearestActiveBarrier(
                out currentBarrier,
                out Vector3 nearestBarrierPosition)
                ? nearestBarrierPosition
                : destination;
            directMovementSpeed = speed;
            return true;
        }

        if (!TryFindBarrierRoute(destination, out NavMeshPath barrierRoute))
        {
            return false;
        }

        if (TryGetRouteEndpoint(barrierRoute, out Vector3 endpoint) &&
            !IsWithinActivationDistance(endpoint) &&
            agent.enabled &&
            agent.isOnNavMesh &&
            queryService.TryApplyPath(agent, barrierRoute, speed))
        {
            hasPendingPushEndpoint = true;
            pendingPushEndpoint = endpoint;
            pendingPushDestination = destination;
            pendingPushSpeed = speed;
            return true;
        }

        hasPendingPushEndpoint = false;
        ActivateDirectMovement(destination, speed);
        return true;
    }

    // Nearest to the enemy itself, not to a far-off destination — this is
    // what the physical push direction should aim at right now, so it
    // never drifts off toward unrelated geometry once a barrier clears.
    private bool TryGetNearestActiveBarrier(
        out ItemNavigationObstacle barrier,
        out Vector3 barrierPosition)
    {
        barrier = FindBestActiveBarrier(ownerTransform.position);

        if (barrier == null)
        {
            barrierPosition = default;
            return false;
        }

        barrierPosition = barrier.transform.position;
        return true;
    }

    private ItemNavigationObstacle FindBestActiveBarrier(Vector3 referencePosition)
    {
        if (itemPusher == null)
        {
            return null;
        }

        ItemNavigationObstacle best = null;
        float bestSqrDistance = float.PositiveInfinity;

        foreach (ItemNavigationObstacle barrier in
                 ItemNavigationObstacle.ActiveServerBarriers)
        {
            if (barrier == null ||
                !barrier.IsBlockingNavigation ||
                !barrier.CanBePushedByEnemyNow ||
                barrier.IsReservedByOtherEnemy(itemPusher.ReservationOwnerId) ||
                IsInFailureCooldown(barrier))
            {
                continue;
            }

            float sqrDistance =
                (barrier.transform.position - referencePosition).sqrMagnitude;

            if (sqrDistance >= bestSqrDistance)
            {
                continue;
            }

            bestSqrDistance = sqrDistance;
            best = barrier;
        }

        return best;
    }

    private bool IsInFailureCooldown(ItemNavigationObstacle barrier)
    {
        return barrier == failedBarrier && Time.time < failedBarrierCooldownUntil;
    }

    private float GetFailedBarrierCooldownDuration()
    {
        return config != null
            ? Mathf.Max(0.1f, config.NavigationProfile.directMovementTimeout)
            : 4f;
    }

    private bool TryFindBarrierRoute(Vector3 destination, out NavMeshPath barrierRoute)
    {
        barrierRoute = null;

        if (agent == null)
        {
            return false;
        }

        ItemNavigationObstacle bestBarrier = FindBestActiveBarrier(destination);

        if (bestBarrier == null)
        {
            return false;
        }

        NavMeshQueryFilter filter = new()
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };
        // The item's own centre sits inside the hole it carves — the
        // sample has to reach past the item's footprint to find any
        // NavMesh at all, or it always fails for anything bigger than
        // the generic navigation sample radius.
        float approachSampleRadius = GetNavigationSampleRadius() +
            bestBarrier.ApproachRadius +
            Mathf.Max(0f, agent.radius);

        if (!queryService.TrySamplePosition(
                bestBarrier.transform.position,
                approachSampleRadius,
                filter,
                out NavMeshHit barrierHit) ||
            !queryService.TryCalculatePath(
                ownerTransform.position,
                barrierHit.position,
                filter,
                barrierRoutePath) ||
            barrierRoutePath.status == NavMeshPathStatus.PathInvalid)
        {
            return false;
        }

        barrierRoute = barrierRoutePath;
        return true;
    }

    // Called every frame regardless of the repath cadence: closing the
    // last stretch to a partial route's endpoint needs to be noticed as
    // soon as it happens, not just whenever the next repath fires.
    public void Tick()
    {
        if (directMovementActive ||
            !hasPendingPushEndpoint ||
            agent == null ||
            agent.pathPending)
        {
            return;
        }

        if (!IsWithinActivationDistance(pendingPushEndpoint))
        {
            return;
        }

        hasPendingPushEndpoint = false;
        ActivateDirectMovement(pendingPushDestination, pendingPushSpeed);
    }

    private static bool TryGetRouteEndpoint(
        NavMeshPath routeSoFar,
        out Vector3 endpoint)
    {
        endpoint = default;

        if (routeSoFar == null ||
            routeSoFar.status == NavMeshPathStatus.PathInvalid)
        {
            return false;
        }

        Vector3[] corners = routeSoFar.corners;

        if (corners == null || corners.Length == 0)
        {
            return false;
        }

        endpoint = corners[^1];
        return true;
    }

    private bool IsWithinActivationDistance(Vector3 point)
    {
        Vector3 delta = point - ownerTransform.position;
        delta.y = 0f;
        float activationDistance = GetDirectMovementActivationDistance();
        return delta.sqrMagnitude <= activationDistance * activationDistance;
    }

    private float GetDirectMovementActivationDistance()
    {
        float stoppingDistance = agent != null
            ? Mathf.Max(0f, agent.stoppingDistance)
            : 0f;
        return Mathf.Max(
            DirectMovementActivationDistance,
            stoppingDistance + 0.05f);
    }

    public bool ContinueDirectMovement(Vector3 destination, float speed)
    {
        if (!directMovementActive)
        {
            return false;
        }

        directMovementDestination = destination;
        directMovementAimPoint = TryGetNearestActiveBarrier(
            out currentBarrier,
            out Vector3 nearestBarrierPosition)
            ? nearestBarrierPosition
            : destination;
        directMovementSpeed = speed;

        if (recoveryController != null &&
            recoveryController.TryRecover(ownerTransform.position))
        {
            if (currentBarrier != null)
            {
                failedBarrier = currentBarrier;
                failedBarrierCooldownUntil =
                    Time.time + GetFailedBarrierCooldownDuration();
            }

            Cancel(restoreAgent: true);
            return false;
        }

        if (itemPusher != null && itemPusher.IsPushingAnyItem)
        {
            return true;
        }

        if (Time.time < nextPathCheckTime)
        {
            return true;
        }

        nextPathCheckTime = Time.time + GetDirectPathCheckInterval();
        BeginQueryBatch();

        if (!TryBuildCompleteRecoveryPath(destination, out Vector3 sampledSource))
        {
            return true;
        }

        if (!TryRestoreAgentAt(sampledSource))
        {
            return true;
        }

        ClearDirectMovementState();
        return false;
    }

    public void Cancel()
    {
        Cancel(restoreAgent: true);
    }

    public void Cancel(bool restoreAgent)
    {
        bool agentNeedsRestore =
            directMovementActive &&
            agent != null &&
            !agent.enabled;

        ClearDirectMovementState();

        if (restoreAgent && agentNeedsRestore)
        {
            TryRestoreAgentNearCurrentPosition();
        }
    }

    public bool HasNavigationBlockerOnPath(NavMeshPath path)
    {
        return itemPusher != null &&
               path != null &&
               itemPusher.HasNavigationBlockerOnRoute(path.corners);
    }

    private void ActivateDirectMovement(Vector3 destination, float speed)
    {
        directMovementDestination = destination;
        directMovementAimPoint = TryGetNearestActiveBarrier(
            out currentBarrier,
            out Vector3 nearestBarrierPosition)
            ? nearestBarrierPosition
            : destination;
        directMovementSpeed = speed;
        directMovementAgentTypeId = agent.agentTypeID;
        directMovementAreaMask = agent.areaMask;
        nextPathCheckTime = Time.time + GetDirectPathCheckInterval();
        directMovementActive = true;
        recoveryController?.Begin(
            ownerTransform.position,
            useDirectMovementTimeout: true);

        if (!agent.enabled)
        {
            return;
        }

        if (agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        agent.enabled = false;
    }

    private bool TryRestoreAgentNearCurrentPosition()
    {
        if (agent == null)
        {
            return false;
        }

        NavMeshQueryFilter filter = new()
        {
            agentTypeID = directMovementAgentTypeId,
            areaMask = directMovementAreaMask
        };

        return queryService.TrySamplePosition(
                   ownerTransform.position,
                   GetNavigationSampleRadius(),
                   filter,
                   out NavMeshHit sourceHit) &&
               TryRestoreAgentAt(sourceHit.position);
    }

    private void ClearDirectMovementState()
    {
        directMovementActive = false;
        directMovementDestination = default;
        directMovementAimPoint = default;
        currentBarrier = null;
        directMovementSpeed = 0f;
        nextPathCheckTime = 0f;
        hasPendingPushEndpoint = false;
        recoveryController?.Reset();
    }

    private bool TryBuildCompleteRecoveryPath(
        Vector3 destination,
        out Vector3 sampledSource)
    {
        sampledSource = ownerTransform.position;
        NavMeshQueryFilter filter = new()
        {
            agentTypeID = directMovementAgentTypeId,
            areaMask = directMovementAreaMask
        };

        if (!queryService.TryBuildPath(
                ownerTransform.position,
                destination,
                GetNavigationSampleRadius(),
                GetNavigationSampleRadius(),
                filter,
                recoveryPath,
                out Vector3 sampledDestination) ||
            !IsSampledDestinationAcceptable(
                destination,
                sampledDestination) ||
            recoveryPath.status != NavMeshPathStatus.PathComplete ||
            HasNavigationBlockerOnPath(recoveryPath) ||
            !queryService.TrySamplePosition(
                ownerTransform.position,
                GetNavigationSampleRadius(),
                filter,
                out NavMeshHit sourceHit))
        {
            return false;
        }

        sampledSource = sourceHit.position;
        return true;
    }

    private bool TryRestoreAgentAt(Vector3 position)
    {
        if (agent == null)
        {
            return false;
        }

        agent.enabled = true;

        if (!agent.Warp(position))
        {
            agent.enabled = false;
            return false;
        }

        return true;
    }

    private void BeginQueryBatch()
    {
        queryService.BeginRepath(
            config != null
                ? config.navigationMaximumPathQueriesPerRepath
                : 24);
    }

    private float GetNavigationSampleRadius()
    {
        return config != null
            ? Mathf.Max(0.1f, config.navigationNavMeshSampleRadius)
            : 2f;
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

    private float GetDirectPathCheckInterval()
    {
        return config != null
            ? Mathf.Max(0.05f, config.navigationDirectPathCheckInterval)
            : 0.2f;
    }
}
