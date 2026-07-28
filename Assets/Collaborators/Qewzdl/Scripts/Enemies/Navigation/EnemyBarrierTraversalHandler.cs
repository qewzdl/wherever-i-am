using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// No complete NavMesh route exists to the destination. The only thing
// that can turn that into a push is a pushable item confirmed to
// actually be reachable — a real NavMesh route whose end genuinely
// arrives near it, not merely the nearest registered barrier to the
// destination in a straight line. That distinction is the whole point:
// accepting "nearest in a straight line" as good enough is what let
// unrelated furniture sitting somewhere else in the level hijack
// push-through in the past, sending the enemy off toward — and into —
// geometry that had nothing to do with the actual blockage.
//
// Once a blocking item is found, this walks toward it normally by
// NavMesh so the enemy still steers around walls instead of cutting a
// straight line, and only drops to direct physical movement for the
// final stretch, where EnemyItemPusher's per-tick physical push
// (FixedUpdate) pushes whatever is actually in front of it. Every
// GetDirectPathCheckInterval seconds it retries a full NavMesh path and
// hands back to normal pathing the moment one opens up. When no
// registered barrier is genuinely reachable, this refuses to engage —
// the caller stops instead of walking the enemy into a wall.
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
    private readonly List<ItemNavigationObstacle> candidateBuffer = new();
    // Walking toward a partial route's endpoint uses plain NavMeshAgent
    // steering, which EnemyNavigator's own stuck-recovery treats as
    // "arrived, nothing to track" the moment remainingDistance gets
    // small — exactly the state a barrier that's genuinely unreachable
    // behind unrelated geometry gets stuck in a few tens of centimetres
    // short of its endpoint, forever. This tracks progress toward that
    // endpoint independently so that specific gap actually gets caught.
    private readonly EnemyNavigationProgressMonitor pendingEndpointProgress = new();

    private EnemyConfig config;
    private bool directMovementActive;
    private Vector3 directMovementDestination;
    // The real requested destination can sit on the far side of a wall
    // that has nothing to do with the barrier being pushed — walking
    // straight at it would ram that wall once the barrier stops being
    // in the way. Physical movement instead aims at the committed
    // barrier's own (possibly-displaced) position; directMovementDestination
    // stays the true goal for HasReached and the periodic normal-path
    // recovery check.
    private Vector3 directMovementAimPoint;
    private ItemNavigationObstacle currentBarrier;
    private float directMovementSpeed;
    private int directMovementAgentTypeId;
    private int directMovementAreaMask;
    private float nextPathCheckTime;

    // A barrier that just timed out stuck-recovery isn't actually
    // pushable in practice (wedged against something else, most often)
    // — without this, the very next route-local search would just find
    // the same barrier again (it's still sitting right there on the
    // route) and the enemy would press into it forever in a tight retry
    // loop. Excluding it briefly gives the route a chance to be
    // re-evaluated as genuinely blocked instead.
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
    private ItemNavigationObstacle pendingPushBarrier;

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

    // Only ever pushes toward a pushable item confirmed to actually be
    // reachable — never just because a complete path couldn't be found
    // for some unrelated reason (permanent walls, an unreachable target
    // island, geometry the NavMesh was never baked to cross). Walks
    // toward it normally by NavMesh so the enemy still steers around
    // walls instead of cutting a straight line, and only drops to
    // direct physical movement once at its end, where EnemyItemPusher
    // takes over pushing.
    public bool TryPushThroughToTarget(Vector3 destination, float speed)
    {
        if (agent == null)
        {
            return false;
        }

        if (directMovementActive)
        {
            if (!TryGetBarrierAimPosition(out Vector3 aimPosition))
            {
                // The committed barrier is gone — destroyed, picked up,
                // or reserved by another enemy. There's nothing left to
                // aim at; cancel and let the next normal move
                // re-evaluate from scratch against whatever route is
                // blocked then, rather than guessing here.
                Cancel(restoreAgent: true);
                return false;
            }

            directMovementDestination = destination;
            directMovementAimPoint = aimPosition;
            directMovementSpeed = speed;
            return true;
        }

        if (!TryFindBlockingBarrierRoute(
                destination,
                out ItemNavigationObstacle bestBarrier,
                out NavMeshPath barrierRoute))
        {
            return false;
        }

        if (TryGetRouteEndpoint(barrierRoute, out Vector3 endpoint) &&
            !IsWithinActivationDistance(endpoint) &&
            agent.enabled &&
            agent.isOnNavMesh &&
            queryService.TryApplyPath(agent, barrierRoute, speed))
        {
            if (!hasPendingPushEndpoint || pendingPushBarrier != bestBarrier)
            {
                pendingEndpointProgress.Begin(ownerTransform.position);
            }

            hasPendingPushEndpoint = true;
            pendingPushEndpoint = endpoint;
            pendingPushDestination = destination;
            pendingPushSpeed = speed;
            pendingPushBarrier = bestBarrier;
            return true;
        }

        hasPendingPushEndpoint = false;
        ActivateDirectMovement(bestBarrier, destination, speed);
        return true;
    }

    // Sticks with whatever barrier is already committed to — its
    // current, possibly-displaced position — for as long as it stays
    // valid, instead of re-evaluating every tick.
    private bool TryGetBarrierAimPosition(out Vector3 aimPosition)
    {
        if (!IsBarrierStillValid(currentBarrier))
        {
            aimPosition = default;
            return false;
        }

        aimPosition = currentBarrier.GetClosestSurfacePoint(ownerTransform.position);
        return true;
    }

    private bool IsBarrierStillValid(ItemNavigationObstacle barrier)
    {
        return barrier != null &&
               barrier.IsBlockingNavigation &&
               barrier.CanBePushedByEnemyNow &&
               itemPusher != null &&
               !barrier.IsReservedByOtherEnemy(itemPusher.ReservationOwnerId) &&
               !IsInFailureCooldown(barrier);
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

    // Considers every registered pushable barrier, nearest to the
    // destination first (a real barricade genuinely blocking the way is
    // typically closer to the destination than unrelated clutter), but
    // only ever accepts one whose route actually arrives near it. A
    // barrier stuck behind unrelated geometry produces a route that
    // gives up wherever the enemy's own reachable area happens to end —
    // which usually is NOT anywhere near that barrier — so the distance
    // check between the route's endpoint and the barrier itself is what
    // rejects it, no matter how close it looked in a straight line to
    // the destination. This is what keeps furniture elsewhere in the
    // level from ever being selected, which is the bug this replaced.
    private bool TryFindBlockingBarrierRoute(
        Vector3 destination,
        out ItemNavigationObstacle blockingBarrier,
        out NavMeshPath barrierRoute)
    {
        barrierRoute = null;
        blockingBarrier = null;

        if (agent == null || itemPusher == null)
        {
            return false;
        }

        candidateBuffer.Clear();

        foreach (ItemNavigationObstacle candidate in
                 ItemNavigationObstacle.ActiveServerBarriers)
        {
            if (IsBarrierStillValid(candidate))
            {
                candidateBuffer.Add(candidate);
            }
        }

        if (candidateBuffer.Count == 0)
        {
            return false;
        }

        candidateBuffer.Sort((a, b) =>
            (a.transform.position - destination).sqrMagnitude.CompareTo(
                (b.transform.position - destination).sqrMagnitude));

        NavMeshQueryFilter filter = new()
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };

        for (int i = 0; i < candidateBuffer.Count; i++)
        {
            ItemNavigationObstacle candidate = candidateBuffer[i];
            // The item's own centre sits inside the hole it carves —
            // the sample has to reach past the item's footprint to find
            // any NavMesh at all, or it always fails for anything
            // bigger than the generic navigation sample radius.
            float approachSampleRadius = GetNavigationSampleRadius() +
                candidate.ApproachRadius +
                Mathf.Max(0f, agent.radius);

            // A Partial status is expected and fine when the barrier's
            // own carve is what's truncating the route (that's exactly
            // the "walk up to what's blocking the way" case) —
            // PathInvalid is an outright rejection.
            if (!queryService.TrySamplePosition(
                    candidate.transform.position,
                    approachSampleRadius,
                    filter,
                    out NavMeshHit barrierHit) ||
                !queryService.TryCalculatePath(
                    ownerTransform.position,
                    barrierHit.position,
                    filter,
                    barrierRoutePath) ||
                barrierRoutePath.status == NavMeshPathStatus.PathInvalid ||
                !TryGetRouteEndpoint(barrierRoutePath, out Vector3 endpoint) ||
                (endpoint - candidate.transform.position).sqrMagnitude >
                approachSampleRadius * approachSampleRadius)
            {
                continue;
            }

            blockingBarrier = candidate;
            barrierRoute = barrierRoutePath;
            return true;
        }

        return false;
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

        if (IsWithinActivationDistance(pendingPushEndpoint))
        {
            hasPendingPushEndpoint = false;
            pendingEndpointProgress.Reset();
            ActivateDirectMovement(
                pendingPushBarrier,
                pendingPushDestination,
                pendingPushSpeed);
            return;
        }

        // A partial route's endpoint can be a dead end the enemy will
        // never actually close the last stretch to (the barrier it was
        // aiming for is behind unrelated geometry, not just past the
        // one carving this exact spot). Ordinary stuck-recovery doesn't
        // catch this: EnemyNavigator's own check backs off the moment
        // remainingDistance looks small, treating "close" as "arrived".
        if (pendingEndpointProgress.IsStuck(
                ownerTransform.position,
                config != null ? config.NavigationProfile : null,
                useDirectMovementTimeout: false))
        {
            if (pendingPushBarrier != null)
            {
                failedBarrier = pendingPushBarrier;
                failedBarrierCooldownUntil =
                    Time.time + GetFailedBarrierCooldownDuration();
            }

            hasPendingPushEndpoint = false;
            pendingEndpointProgress.Reset();
        }
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

        if (!TryGetBarrierAimPosition(out Vector3 aimPosition))
        {
            // The committed barrier is gone — nothing left to push.
            // Cancel and let the next normal move re-evaluate against
            // whatever route is blocked then.
            Cancel(restoreAgent: true);
            return false;
        }

        directMovementDestination = destination;
        directMovementAimPoint = aimPosition;
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

    private void ActivateDirectMovement(
        ItemNavigationObstacle barrier,
        Vector3 destination,
        float speed)
    {
        currentBarrier = barrier;
        directMovementDestination = destination;
        directMovementAimPoint = barrier != null
            ? barrier.GetClosestSurfacePoint(ownerTransform.position)
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
        pendingPushBarrier = null;
        pendingEndpointProgress.Reset();
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
