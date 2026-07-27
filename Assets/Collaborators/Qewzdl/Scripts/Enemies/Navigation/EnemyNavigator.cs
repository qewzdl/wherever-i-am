using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyNavigator : MonoBehaviour, IEnemyDirectMovementIntentSource
{
    private const float NavMeshSampleRadius = 2f;
    private const float MinimumStandingWaypointDistance = 0.1f;
    private const float DirectMovementActivationDistance = 0.2f;
    private const float DirectPathCheckInterval = 0.2f;
    private const float PushFallbackConfirmationDuration = 0.5f;
    private const float BlockedRoutePlanInterval = 0.25f;

    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private EnemyPostureController postureController;
    [SerializeField] private EnemyDoorInteractor doorInteractor;
    [SerializeField] private EnemyItemPusher itemPusher;

    private NavMeshPath pathBuffer;
    private NavMeshPath posturePathBuffer;
    private NavMeshPath crawlingRouteBuffer;
    private NavMeshPath standingCandidatePathBuffer;
    private NavMeshPath directRecoveryPathBuffer;
    private NavMeshPath blockedRoutePathBuffer;
    private EnemyBlockedRoutePlanner blockedRoutePlanner;

    private EnemyConfig config;
    private bool warnedAboutMissingNavMesh;

    private bool hasRequestedNavigation;
    private Vector3 requestedNavigationDestination;
    private float requestedNavigationSpeed;

    private bool barrierApproachActive;
    private Vector3 barrierApproachEndpoint;
    private Vector3 barrierApproachDestination;
    private float barrierApproachSpeed;
    private float barrierApproachPushAllowedTime;
    private bool barrierApproachUsesDirectCorridor;
    private Vector3 barrierApproachPushDestination;
    private ItemNavigationObstacle barrierApproachBarrier;

    private bool directMovementActive;
    private Vector3 directMovementDestination;
    private float directMovementSpeed;
    private int directMovementAgentTypeId;
    private int directMovementAreaMask;
    private float nextDirectPathCheckTime;
    private bool directMovementTracksNavigationDestination;

    private float nextStandingRecoveryCheckTime;
    private float nextBlockedRoutePlanTime;

    public Vector3 Position => transform.position;

    public bool TryGetEnemyDirectMovementIntent(
        out Vector3 destination,
        out float speed)
    {
        destination = directMovementDestination;
        speed = directMovementSpeed;
        return directMovementActive;
    }

    public bool IsDirectApproachBlockedByItem(Vector3 destination)
    {
        return itemPusher != null &&
               itemPusher.IsDirectApproachBlockedByItem(destination);
    }

    private void Awake()
    {
        CacheComponents();

        pathBuffer = new NavMeshPath();
        posturePathBuffer = new NavMeshPath();
        crawlingRouteBuffer = new NavMeshPath();
        standingCandidatePathBuffer = new NavMeshPath();
        directRecoveryPathBuffer = new NavMeshPath();
        blockedRoutePathBuffer = new NavMeshPath();
        blockedRoutePlanner = new EnemyBlockedRoutePlanner();
    }

    private void Update()
    {
        if (!barrierApproachActive || directMovementActive)
        {
            return;
        }

        if (agent == null ||
            !agent.enabled ||
            !agent.isOnNavMesh ||
            agent.pathPending)
        {
            return;
        }

        if (!barrierApproachUsesDirectCorridor)
        {
            Vector3 toEndpoint =
                barrierApproachEndpoint - transform.position;
            toEndpoint.y = 0f;

            float activationDistance = GetDirectMovementActivationDistance();

            if (toEndpoint.sqrMagnitude >
                activationDistance * activationDistance)
            {
                return;
            }
        }

        if (TryResumeCompleteNavMeshRoute(
                barrierApproachDestination,
                barrierApproachSpeed))
        {
            return;
        }

        if (!HasConfirmedPushableBarrier())
        {
            StopBarrierApproach();
            return;
        }

        if (Time.time < barrierApproachPushAllowedTime)
        {
            return;
        }

        ActivateDirectMovement(
            barrierApproachPushDestination,
            barrierApproachBarrier == null,
            barrierApproachSpeed);
    }

    public void Configure(EnemyConfig config)
    {
        this.config = config;

        CacheComponents();

        hasRequestedNavigation = false;
        CancelBarrierTraversal(restoreAgent: true);
        nextStandingRecoveryCheckTime = 0f;
        nextBlockedRoutePlanTime = 0f;
        doorInteractor?.CancelActiveInteraction();

        if (config == null)
        {
            return;
        }

        postureController?.Configure(config);

        if (agent == null)
        {
            return;
        }

        agent.speed = config.patrolSpeed;
        agent.acceleration = config.acceleration;
        agent.angularSpeed = config.angularSpeed;
        agent.stoppingDistance = config.stoppingDistance;

        if (postureController != null)
        {
            postureController.TrySetServerPosture(EnemyPosture.Standing);
            nextStandingRecoveryCheckTime = 0f;
        }
    }

    public void DisableAgent()
    {
        CacheComponents();

        hasRequestedNavigation = false;
        CancelBarrierTraversal(restoreAgent: false);
        doorInteractor?.CancelActiveInteraction();

        if (agent != null)
        {
            agent.enabled = false;
        }
    }

    public bool TryMoveTo(Vector3 destination, float speed)
    {
        RememberRequestedNavigation(destination, speed);

        if (TryResolveDoorNavigation(destination, out EnemyDoorNavigationResult doorResult))
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
        if (doorInteractor == null)
        {
            return;
        }

        if (!hasRequestedNavigation)
        {
            doorInteractor.CancelActiveInteraction();
            return;
        }

        bool hadActiveInteraction = doorInteractor.HasActiveInteraction;
        Vector3 probeDestination = GetDoorNavigationProbeDestination();
        EnemyDoorNavigationResult doorResult = doorInteractor.EvaluateNavigation(
            transform.position,
            probeDestination
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
                requestedNavigationSpeed
            );

            return;
        }

        if (hadActiveInteraction)
        {
            TryMoveAfterDoorNavigation(
                requestedNavigationDestination,
                requestedNavigationSpeed
            );
        }
    }

    private bool TryMoveAfterDoorNavigation(Vector3 destination, float speed)
    {
        if (postureController != null && postureController.IsPostureTransitionInProgress)
        {
            StopForPostureTransition();
            return false;
        }

        if (directMovementActive &&
            ContinueDirectMovement(destination, speed))
        {
            return true;
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

        bool movedWithCurrentPosture =
            TryMoveToWithCurrentPosture(destination, speed);

        if (!movedWithCurrentPosture)
        {
            StopAtBlockedRoute();
        }

        return movedWithCurrentPosture;
    }

    public void Stop()
    {
        hasRequestedNavigation = false;
        CancelBarrierTraversal(restoreAgent: true);
        doorInteractor?.CancelActiveInteraction();

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
        doorInteractor?.CancelActiveInteraction();

        if (!TryEnsureOnNavMesh())
        {
            return;
        }

        agent.ResetPath();
    }

    public bool HasReached(float reachDistance)
    {
        if (doorInteractor != null && doorInteractor.HasActiveInteraction)
        {
            return false;
        }

        if (directMovementActive)
        {
            Vector3 delta = directMovementDestination - transform.position;
            delta.y = 0f;
            return delta.sqrMagnitude <= reachDistance * reachDistance;
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
            if (directMovementActive)
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
        out EnemyDoorNavigationResult doorResult
    )
    {
        doorResult = EnemyDoorNavigationResult.None;

        if (doorInteractor == null)
        {
            return false;
        }

        doorResult = doorInteractor.EvaluateNavigation(transform.position, destination);
        return doorResult.IsHandled;
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
        bool canTryStanding = !postureController.IsCrawling || CanAttemptStandingRecovery();

        if (canTryStanding && TryMoveStandingFirst(destination, speed))
        {
            return true;
        }

        if (postureController.IsPostureTransitionInProgress)
        {
            StopForPostureTransition();
            return false;
        }

        return TryMoveToWithPosture(destination, speed, EnemyPosture.Crawling);
    }

    private bool TryMoveStandingFirst(Vector3 destination, float speed)
    {
        if (!postureController.CanUsePostureAtCurrentPosition(EnemyPosture.Standing))
        {
            return false;
        }

        if (TryMoveToWithPosture(destination, speed, EnemyPosture.Standing))
        {
            return true;
        }

        if (postureController.IsPostureTransitionInProgress)
        {
            StopForPostureTransition();
            return false;
        }

        if (!TryFindStandingWaypointTowards(destination, out Vector3 standingWaypoint))
        {
            return false;
        }

        return TryMoveToWithPosture(standingWaypoint, speed, EnemyPosture.Standing);
    }

    private bool TryFindStandingWaypointTowards(Vector3 destination, out Vector3 waypoint)
    {
        waypoint = transform.position;

        if (agent == null || postureController == null)
        {
            return false;
        }

        if (!postureController.CanUsePostureAtCurrentPosition(EnemyPosture.Standing))
        {
            return false;
        }

        if (!TryBuildCompletePathForPosture(
                destination,
                EnemyPosture.Crawling,
                crawlingRouteBuffer
            ))
        {
            return false;
        }

        Vector3[] corners = crawlingRouteBuffer.corners;

        if (corners == null || corners.Length < 2)
        {
            return false;
        }

        float sampleStep = config != null
            ? Mathf.Max(0.1f, config.postureNavMeshSampleRadius)
            : 1f;

        float minimumWaypointDistance = agent != null
            ? Mathf.Max(agent.stoppingDistance, MinimumStandingWaypointDistance)
            : MinimumStandingWaypointDistance;

        float minimumWaypointSqrDistance = minimumWaypointDistance * minimumWaypointDistance;

        for (int segmentIndex = corners.Length - 2; segmentIndex >= 0; segmentIndex--)
        {
            Vector3 segmentStart = corners[segmentIndex];
            Vector3 segmentEnd = corners[segmentIndex + 1];

            float segmentLength = Vector3.Distance(segmentStart, segmentEnd);
            int sampleCount = Mathf.Max(1, Mathf.CeilToInt(segmentLength / sampleStep));

            for (int sampleIndex = sampleCount; sampleIndex >= 0; sampleIndex--)
            {
                float t = sampleIndex / (float)sampleCount;
                Vector3 candidate = Vector3.Lerp(segmentStart, segmentEnd, t);

                Vector3 flatDelta = candidate - transform.position;
                flatDelta.y = 0f;

                if (flatDelta.sqrMagnitude <= minimumWaypointSqrDistance)
                {
                    continue;
                }

                if (!TryBuildCompletePathForPosture(
                        candidate,
                        EnemyPosture.Standing,
                        standingCandidatePathBuffer
                    ))
                {
                    continue;
                }

                waypoint = candidate;
                return true;
            }
        }

        return false;
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

    private bool TryMoveToWithPosture(
        Vector3 destination,
        float baseSpeed,
        EnemyPosture posture
    )
    {
        if (postureController == null)
        {
            return false;
        }

        if (!TryBuildCompletePathForPosture(destination, posture, posturePathBuffer))
        {
            float partialPathSpeed = postureController.GetSpeedForPosture(
                baseSpeed,
                posture
            );

            if (IsCompletePathBlockedByNavigationItem(posturePathBuffer))
            {
                if (TryMoveToReachablePushBarrier(
                        destination,
                        partialPathSpeed))
                {
                    return true;
                }

                StopForPendingItemRoute();
                return true;
            }

            if (posture == EnemyPosture.Standing)
            {
                if (TryBuildCompletePathForPosture(
                        destination,
                        EnemyPosture.Crawling,
                        crawlingRouteBuffer))
                {
                    return false;
                }

                if (IsCompletePathBlockedByNavigationItem(
                        crawlingRouteBuffer))
                {
                    if (TryMoveToReachablePushBarrier(
                            destination,
                            partialPathSpeed))
                    {
                        return true;
                    }

                    StopForPendingItemRoute();
                    return true;
                }
            }

            return postureController.CurrentPosture == posture &&
                   TryMoveToPushableBarrier(
                       destination,
                       partialPathSpeed,
                       posturePathBuffer
                   );
        }

        EnemyPosture previousPosture = postureController.CurrentPosture;

        if (!postureController.TrySetServerPosture(posture))
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
            HandlePostureChanged(postureController.CurrentPosture);
        }

        float postureSpeed = postureController.GetSpeedForPosture(baseSpeed, posture);
        return TryMoveToWithCurrentPosture(destination, postureSpeed);
    }

    private bool TryMoveToWithCurrentPosture(Vector3 destination, float speed)
    {
        if (!TryEnsureOnNavMesh())
        {
            return false;
        }

        if (!TryBuildCompletePath(destination, out Vector3 sampledDestination))
        {
            if (IsCompletePathBlockedByNavigationItem(pathBuffer))
            {
                if (TryMoveToReachablePushBarrier(destination, speed))
                {
                    return true;
                }

                StopForPendingItemRoute();
                return true;
            }

            return TryMoveToPushableBarrier(destination, speed, pathBuffer);
        }

        ClearBarrierApproach();
        agent.isStopped = false;
        agent.speed = speed;

        return agent.SetDestination(sampledDestination);
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

    private void StopForPendingItemRoute()
    {
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
        pathBuffer.ClearCorners();

        if (!TrySamplePositionForCurrentAgent(destination, out NavMeshHit hit))
        {
            return false;
        }

        sampledDestination = hit.position;

        if (!agent.CalculatePath(sampledDestination, pathBuffer))
        {
            return false;
        }

        return pathBuffer.status == NavMeshPathStatus.PathComplete &&
               !HasNavigationBlockerOnPath(pathBuffer);
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

        return NavMesh.SamplePosition(sourcePosition, out hit, NavMeshSampleRadius, filter);
    }

    private bool TryBuildCompletePathForPosture(
        Vector3 destination,
        EnemyPosture posture,
        NavMeshPath targetPath
    )
    {
        if (agent == null || postureController == null || targetPath == null)
        {
            return false;
        }

        targetPath.ClearCorners();

        if (!postureController.CanUsePostureAtCurrentPosition(posture))
        {
            return false;
        }

        NavMeshQueryFilter filter = new()
        {
            agentTypeID = postureController.GetAgentTypeIdForPosture(posture),
            areaMask = agent.areaMask
        };

        float sourceSampleRadius = config != null
            ? Mathf.Max(0.05f, config.postureSwitchSampleRadius)
            : 0.25f;

        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit sourceHit, sourceSampleRadius, filter))
        {
            return false;
        }

        float destinationSampleRadius = GetDestinationSampleRadiusForPosture(posture);

        if (!postureController.TryGetUsablePosturePosition(
                posture,
                destination,
                destinationSampleRadius,
                out NavMeshHit destinationHit
            ))
        {
            return false;
        }

        if (!NavMesh.CalculatePath(sourceHit.position, destinationHit.position, filter, targetPath))
        {
            return false;
        }

        return targetPath.status == NavMeshPathStatus.PathComplete &&
               !HasNavigationBlockerOnPath(targetPath);
    }

    private bool TryMoveToPushableBarrier(
        Vector3 destination,
        float speed,
        NavMeshPath partialPath)
    {
        if (agent == null ||
            itemPusher == null ||
            partialPath == null ||
            !CanUsePushFallback(partialPath.status))
        {
            return false;
        }

        if (barrierApproachActive)
        {
            barrierApproachDestination = destination;
            barrierApproachSpeed = speed;

            if (barrierApproachBarrier == null)
            {
                barrierApproachPushDestination = destination;
            }

            return true;
        }

        Vector3[] corners = partialPath.corners;
        int cornerCount = corners != null ? corners.Length : 0;

        // At a carved boundary Unity can return a partial path containing only
        // the agent position. A nearby pushable item validates this fallback,
        // while the gameplay destination remains the movement target.
        Vector3 endpoint = cornerCount > 0
            ? corners[^1]
            : transform.position;
        Vector3 routeDirection = destination - endpoint;
        routeDirection.y = 0f;

        if (routeDirection.sqrMagnitude < 0.0001f && cornerCount >= 2)
        {
            routeDirection = endpoint - corners[^2];
            routeDirection.y = 0f;
        }

        bool usesDirectCorridor = false;

        if (!itemPusher.HasPushableNavigationBarrierNear(
                endpoint,
                routeDirection))
        {
            if (!itemPusher.HasDirectlyReachablePushableBarrier(
                    destination))
            {
                return TryMoveToReachablePushBarrier(destination, speed);
            }

            endpoint = transform.position;
            cornerCount = 0;
            usesDirectCorridor = true;
        }

        agent.speed = speed;

        Vector3 toEndpoint = endpoint - transform.position;
        toEndpoint.y = 0f;
        float activationDistance = GetDirectMovementActivationDistance();

        if (cornerCount >= 2 &&
            toEndpoint.sqrMagnitude >
            activationDistance * activationDistance)
        {
            agent.isStopped = false;

            if (!agent.SetPath(partialPath))
            {
                return false;
            }

            BeginBarrierApproach(
                endpoint,
                destination,
                destination,
                speed,
                usesDirectCorridor,
                null);
        }
        else
        {
            agent.isStopped = true;
            agent.ResetPath();
            BeginBarrierApproach(
                endpoint,
                destination,
                destination,
                speed,
                usesDirectCorridor,
                null);
        }

        return true;
    }

    private bool TryMoveToReachablePushBarrier(
        Vector3 destination,
        float speed)
    {
        if (barrierApproachActive)
        {
            barrierApproachDestination = destination;
            barrierApproachSpeed = speed;
            return true;
        }

        if (!TryEnsureOnNavMesh() || itemPusher == null)
        {
            return false;
        }

        if (Time.time < nextBlockedRoutePlanTime)
        {
            return false;
        }

        nextBlockedRoutePlanTime =
            Time.time + BlockedRoutePlanInterval;

        blockedRoutePlanner ??= new EnemyBlockedRoutePlanner();
        blockedRoutePathBuffer ??= new NavMeshPath();

        NavMeshQueryFilter filter = new()
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };

        if (!blockedRoutePlanner.TryBuildPlan(
                transform.position,
                destination,
                filter,
                agent.radius,
                itemPusher,
                blockedRoutePathBuffer,
                out EnemyBlockedRoutePlan plan))
        {
            return false;
        }

        agent.speed = speed;
        agent.isStopped = false;

        if (!agent.SetPath(blockedRoutePathBuffer))
        {
            return false;
        }

        BeginBarrierApproach(
            plan.ApproachEndpoint,
            destination,
            plan.PushDestination,
            speed,
            usesDirectCorridor: false,
            barrier: plan.Barrier);
        return true;
    }

    private bool ContinueDirectMovement(Vector3 destination, float speed)
    {
        if (directMovementTracksNavigationDestination)
        {
            directMovementDestination = destination;
        }

        directMovementSpeed = speed;

        if (itemPusher != null && itemPusher.IsPushingAnyItem)
        {
            return true;
        }

        if (Time.time < nextDirectPathCheckTime)
        {
            return true;
        }

        nextDirectPathCheckTime =
            Time.time + DirectPathCheckInterval;

        if (!TryBuildCompleteDirectRecoveryPath(
                destination,
                out Vector3 sampledSource))
        {
            if (!directMovementTracksNavigationDestination &&
                HasReachedDirectMovementDestination() &&
                TryRestoreAgentNearCurrentPosition())
            {
                ClearDirectMovementState();
                return false;
            }

            return true;
        }

        if (!TryRestoreAgentAt(sampledSource))
        {
            return true;
        }

        ClearDirectMovementState();
        return false;
    }

    private void ActivateDirectMovement(
        Vector3 movementDestination,
        bool tracksNavigationDestination,
        float speed)
    {
        if (agent == null)
        {
            return;
        }

        ClearBarrierApproach();
        directMovementDestination = movementDestination;
        directMovementSpeed = speed;
        directMovementTracksNavigationDestination =
            tracksNavigationDestination;
        directMovementAgentTypeId = agent.agentTypeID;
        directMovementAreaMask = agent.areaMask;
        nextDirectPathCheckTime =
            Time.time + DirectPathCheckInterval;
        directMovementActive = true;

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

    private bool HasReachedDirectMovementDestination()
    {
        Vector3 delta = directMovementDestination - transform.position;
        delta.y = 0f;
        float reachDistance = GetDirectMovementActivationDistance();
        return delta.sqrMagnitude <= reachDistance * reachDistance;
    }

    private bool TryRestoreAgentNearCurrentPosition()
    {
        NavMeshQueryFilter filter = new()
        {
            agentTypeID = directMovementAgentTypeId,
            areaMask = directMovementAreaMask
        };

        return NavMesh.SamplePosition(
                   transform.position,
                   out NavMeshHit sourceHit,
                   NavMeshSampleRadius,
                   filter) &&
               TryRestoreAgentAt(sourceHit.position);
    }

    private void CancelBarrierTraversal(bool restoreAgent)
    {
        bool agentNeedsRestore =
            directMovementActive &&
            agent != null &&
            !agent.enabled;

        ClearBarrierApproach();
        ClearDirectMovementState();

        if (restoreAgent && agentNeedsRestore)
        {
            TryEnsureOnNavMesh();
        }
    }

    private void BeginBarrierApproach(
        Vector3 endpoint,
        Vector3 destination,
        Vector3 pushDestination,
        float speed,
        bool usesDirectCorridor,
        ItemNavigationObstacle barrier)
    {
        bool startsBarrierConfirmation = !barrierApproachActive;

        barrierApproachEndpoint = endpoint;
        barrierApproachDestination = destination;
        barrierApproachPushDestination = pushDestination;
        barrierApproachSpeed = speed;
        barrierApproachUsesDirectCorridor = usesDirectCorridor;
        barrierApproachBarrier = barrier;
        barrierApproachActive = true;

        if (startsBarrierConfirmation)
        {
            barrierApproachPushAllowedTime =
                Time.time + PushFallbackConfirmationDuration;
        }
    }

    private bool HasConfirmedPushableBarrier()
    {
        if (itemPusher == null)
        {
            return false;
        }

        if (barrierApproachBarrier != null)
        {
            return itemPusher.HasDirectlyReachablePushableBarrier(
                barrierApproachBarrier,
                barrierApproachPushDestination);
        }

        if (barrierApproachUsesDirectCorridor)
        {
            return itemPusher.HasDirectlyReachablePushableBarrier(
                barrierApproachDestination);
        }

        Vector3 routeDirection =
            barrierApproachDestination - barrierApproachEndpoint;
        routeDirection.y = 0f;

        return itemPusher.HasPushableNavigationBarrierNear(
            barrierApproachEndpoint,
            routeDirection);
    }

    private void StopBarrierApproach()
    {
        ClearBarrierApproach();

        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        agent.isStopped = true;
        agent.ResetPath();
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

    private void ClearBarrierApproach()
    {
        barrierApproachActive = false;
        barrierApproachEndpoint = default;
        barrierApproachDestination = default;
        barrierApproachPushDestination = default;
        barrierApproachSpeed = 0f;
        barrierApproachPushAllowedTime = float.PositiveInfinity;
        barrierApproachUsesDirectCorridor = false;
        barrierApproachBarrier = null;
    }

    private void ClearDirectMovementState()
    {
        directMovementActive = false;
        directMovementDestination = default;
        directMovementSpeed = 0f;
        nextDirectPathCheckTime = 0f;
        directMovementTracksNavigationDestination = false;
    }

    private bool TryBuildCompleteDirectRecoveryPath(
        Vector3 destination,
        out Vector3 sampledSource)
    {
        sampledSource = transform.position;
        directRecoveryPathBuffer ??= new NavMeshPath();
        directRecoveryPathBuffer.ClearCorners();

        NavMeshQueryFilter filter = new()
        {
            agentTypeID = directMovementAgentTypeId,
            areaMask = directMovementAreaMask
        };

        if (!NavMesh.SamplePosition(
                transform.position,
                out NavMeshHit sourceHit,
                NavMeshSampleRadius,
                filter) ||
            !NavMesh.SamplePosition(
                destination,
                out NavMeshHit destinationHit,
                NavMeshSampleRadius,
                filter) ||
            !NavMesh.CalculatePath(
                sourceHit.position,
                destinationHit.position,
                filter,
                directRecoveryPathBuffer) ||
            directRecoveryPathBuffer.status !=
            NavMeshPathStatus.PathComplete ||
            HasNavigationBlockerOnPath(directRecoveryPathBuffer))
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

        warnedAboutMissingNavMesh = false;
        return true;
    }

    private bool TryResumeCompleteNavMeshRoute(
        Vector3 destination,
        float speed)
    {
        if (!TryBuildCompletePath(
                destination,
                out Vector3 sampledDestination))
        {
            return false;
        }

        ClearBarrierApproach();
        agent.isStopped = false;
        agent.speed = speed;
        return agent.SetDestination(sampledDestination);
    }

    private bool HasNavigationBlockerOnPath(NavMeshPath path)
    {
        return itemPusher != null &&
               path != null &&
               itemPusher.HasNavigationBlockerOnRoute(path.corners);
    }

    private bool IsCompletePathBlockedByNavigationItem(NavMeshPath path)
    {
        return path != null &&
               path.status == NavMeshPathStatus.PathComplete &&
               HasNavigationBlockerOnPath(path);
    }

    private static bool CanUsePushFallback(NavMeshPathStatus pathStatus)
    {
        return pathStatus == NavMeshPathStatus.PathPartial ||
               pathStatus == NavMeshPathStatus.PathInvalid;
    }

    private float GetDestinationSampleRadiusForPosture(EnemyPosture posture)
    {
        if (config == null)
        {
            return NavMeshSampleRadius;
        }

        if (posture == EnemyPosture.Standing)
        {
            return Mathf.Max(0.05f, config.postureSwitchSampleRadius);
        }

        return Mathf.Max(0.1f, config.postureNavMeshSampleRadius);
    }

    private bool CanAttemptStandingRecovery()
    {
        if (config == null || postureController == null)
        {
            return false;
        }

        if (postureController.IsPostureTransitionInProgress)
        {
            return false;
        }

        if (!postureController.IsCrawling)
        {
            return true;
        }

        float now = Time.time;
        float minPostureDuration = Mathf.Max(0f, config.minPostureDuration);
        float nextAllowedByDuration = postureController.LastPostureChangedTime + minPostureDuration;

        if (now < nextAllowedByDuration)
        {
            nextStandingRecoveryCheckTime = Mathf.Max(
                nextStandingRecoveryCheckTime,
                nextAllowedByDuration
            );

            return false;
        }

        if (now < nextStandingRecoveryCheckTime)
        {
            return false;
        }

        nextStandingRecoveryCheckTime = now + Mathf.Max(
            0.05f,
            config.standingRecoveryCheckInterval
        );

        return true;
    }

    private void HandlePostureChanged(EnemyPosture posture)
    {
        if (config == null)
        {
            nextStandingRecoveryCheckTime = 0f;
            return;
        }

        float now = Time.time;

        if (posture == EnemyPosture.Crawling)
        {
            nextStandingRecoveryCheckTime = now + Mathf.Max(0f, config.minPostureDuration);
            return;
        }

        nextStandingRecoveryCheckTime = 0f;
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
