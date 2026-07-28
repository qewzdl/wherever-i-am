using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyNavigator : MonoBehaviour, IEnemyDirectMovementIntentSource
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
    private EnemyBarrierTraversalHandler barrierTraversal;

    private EnemyConfig config;
    private bool warnedAboutMissingNavMesh;

    private bool hasRequestedNavigation;
    private Vector3 requestedNavigationDestination;
    private float requestedNavigationSpeed;

    public Vector3 Position => transform.position;
    public EnemyNavigationQueryTelemetrySnapshot QueryTelemetry =>
        queryTelemetry != null
            ? queryTelemetry.Snapshot
            : default;

    public bool TryGetEnemyDirectMovementIntent(
        out Vector3 destination,
        out float speed)
    {
        if (barrierTraversal != null)
        {
            return barrierTraversal.TryGetDirectMovementIntent(
                out destination,
                out speed);
        }

        destination = default;
        speed = 0f;
        return false;
    }

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
        barrierTraversal = new EnemyBarrierTraversalHandler(
            transform,
            agent,
            itemPusher,
            queryService,
            recoveryController);

        pathBuffer = new NavMeshPath();
    }

    private void Update()
    {
        barrierTraversal?.Tick();
    }

    public void Configure(EnemyConfig config)
    {
        this.config = config;

        CacheComponents();

        hasRequestedNavigation = false;
        CancelBarrierTraversal(restoreAgent: true);
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
        barrierTraversal?.Configure(config);

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
        CancelBarrierTraversal(restoreAgent: false);
        doorTraversal?.Cancel();

        if (agent != null)
        {
            agent.enabled = false;
        }
    }

    public bool TryMoveTo(Vector3 destination, float speed)
    {
        RememberRequestedNavigation(destination, speed);

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

        if (barrierTraversal != null &&
            barrierTraversal.IsDirectMovementActive &&
            barrierTraversal.ContinueDirectMovement(destination, speed))
        {
            return true;
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

        if (config != null && config.crawlingEnabled && postureController != null)
        {
            bool moved = TryMoveWithPosturePriority(destination, speed);

            if (!moved && !postureController.IsPostureTransitionInProgress)
            {
                StopAtBlockedRoute();
            }

            return moved;
        }

        bool movedWithCurrentPosture =
            TryMoveToWithCurrentPosture(destination, speed);

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

        recoveryController.EnsureTracking(
            transform.position,
            useDirectMovementTimeout: false);

        if (!recoveryController.TryRecover(transform.position))
        {
            return false;
        }

        CancelBarrierTraversal(restoreAgent: true);

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
        CancelBarrierTraversal(restoreAgent: true);
        doorTraversal?.Cancel();
        repathScheduler?.Invalidate();
        recoveryController?.Reset();

        if (!TryEnsureOnNavMesh())
        {
            return;
        }

        agent.isStopped = true;
    }

    public void ResetPath()
    {
        hasRequestedNavigation = false;
        CancelBarrierTraversal(restoreAgent: true);
        doorTraversal?.Cancel();
        repathScheduler?.Invalidate();
        recoveryController?.Reset();

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

        if (barrierTraversal != null &&
            barrierTraversal.IsDirectMovementActive)
        {
            return barrierTraversal.HasReached(reachDistance);
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
            if (barrierTraversal != null &&
                barrierTraversal.IsDirectMovementActive)
            {
                // The brain uses this method as a locomotion readiness gate.
                // Direct physics movement is valid even though the agent is
                // intentionally detached from NavMesh.
                return true;
            }

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

    private void RememberRequestedNavigation(Vector3 destination, float speed)
    {
        requestedNavigationDestination = destination;
        requestedNavigationSpeed = speed;
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
        CancelBarrierTraversal(restoreAgent: true);

        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        agent.isStopped = true;
    }

    private bool TryMoveWithPosturePriority(Vector3 destination, float speed)
    {
        if (postureTraversal == null ||
            !postureTraversal.TryBuildPlan(
                destination,
                out EnemyPostureTraversalPlan plan))
        {
            return false;
        }

        float postureSpeed = postureController.GetSpeedForPosture(
            speed,
            plan.Posture);

        if (plan.IsBlockedByItem)
        {
            return TryPushThroughToTarget(destination, postureSpeed);
        }

        if (!plan.IsComplete)
        {
            if (postureController.CurrentPosture != plan.Posture)
            {
                return false;
            }

            return TryPushThroughToTarget(destination, postureSpeed);
        }

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

        CancelBarrierTraversal(restoreAgent: true);
        return queryService.TryApplyPath(
            agent,
            plan.Path,
            postureSpeed);
    }

    private void StopForPostureTransition()
    {
        CancelBarrierTraversal(restoreAgent: true);

        if (!TryEnsureOnNavMesh())
        {
            return;
        }

        agent.isStopped = true;
    }

    private bool TryMoveToWithCurrentPosture(Vector3 destination, float speed)
    {
        if (!TryEnsureOnNavMesh())
        {
            return false;
        }

        if (!TryBuildCompletePath(destination, out _))
        {
            return TryPushThroughToTarget(destination, speed);
        }

        CancelBarrierTraversal(restoreAgent: true);
        return queryService.TryApplyPath(agent, pathBuffer, speed);
    }

    private void StopAtBlockedRoute()
    {
        CancelBarrierTraversal(restoreAgent: true);

        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
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

    private bool TryPushThroughToTarget(Vector3 destination, float speed)
    {
        return barrierTraversal != null &&
               barrierTraversal.TryPushThroughToTarget(destination, speed);
    }

    private void CancelBarrierTraversal(bool restoreAgent)
    {
        barrierTraversal?.Cancel(restoreAgent);
    }

    private bool HasNavigationBlockerOnPath(NavMeshPath path)
    {
        return barrierTraversal != null &&
               barrierTraversal.HasNavigationBlockerOnPath(path);
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
