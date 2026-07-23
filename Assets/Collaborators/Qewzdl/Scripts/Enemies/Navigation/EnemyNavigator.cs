using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyNavigator : MonoBehaviour, IEnemyPushNavigationIntentSource
{
    private const float NavMeshSampleRadius = 2f;
    private const float MinimumStandingWaypointDistance = 0.1f;

    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private EnemyPostureController postureController;
    [SerializeField] private EnemyDoorInteractor doorInteractor;
    [SerializeField] private EnemyItemPusher itemPusher;

    private NavMeshPath pathBuffer;
    private NavMeshPath posturePathBuffer;
    private NavMeshPath crawlingRouteBuffer;
    private NavMeshPath standingCandidatePathBuffer;

    private EnemyConfig config;
    private bool warnedAboutMissingNavMesh;

    private bool hasRequestedNavigation;
    private Vector3 requestedNavigationDestination;
    private float requestedNavigationSpeed;

    private float nextStandingRecoveryCheckTime;

    public Vector3 Position => transform.position;

    public bool TryGetEnemyPushNavigationIntent(out Vector3 destination)
    {
        destination = requestedNavigationDestination;
        return hasRequestedNavigation;
    }

    private void Awake()
    {
        CacheComponents();

        pathBuffer = new NavMeshPath();
        posturePathBuffer = new NavMeshPath();
        crawlingRouteBuffer = new NavMeshPath();
        standingCandidatePathBuffer = new NavMeshPath();
    }

    public void Configure(EnemyConfig config)
    {
        this.config = config;

        CacheComponents();

        hasRequestedNavigation = false;
        itemPusher?.CancelAuthorizedPush();
        nextStandingRecoveryCheckTime = 0f;
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
        itemPusher?.CancelAuthorizedPush();
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

        if (config != null && config.crawlingEnabled && postureController != null)
        {
            return TryMoveWithPosturePriority(destination, speed);
        }

        return TryMoveToWithCurrentPosture(destination, speed);
    }

    public void Stop()
    {
        hasRequestedNavigation = false;
        itemPusher?.CancelAuthorizedPush();
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
        itemPusher?.CancelAuthorizedPush();
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

        if (!TryEnsureOnNavMesh())
        {
            return false;
        }

        return !agent.pathPending && agent.remainingDistance <= reachDistance;
    }

    public bool TryEnsureOnNavMesh()
    {
        CacheComponents();

        if (agent == null || !agent.enabled || !agent.gameObject.activeInHierarchy)
        {
            return false;
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
        itemPusher?.CancelAuthorizedPush();

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
        itemPusher?.CancelAuthorizedPush();

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
            return TryMoveToPushableBarrier(destination, speed, pathBuffer);
        }

        CancelPushAuthorizationIfIdle();
        agent.isStopped = false;
        agent.speed = speed;

        return agent.SetDestination(sampledDestination);
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

        return pathBuffer.status == NavMeshPathStatus.PathComplete;
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

        return targetPath.status == NavMeshPathStatus.PathComplete;
    }

    private bool TryMoveToPushableBarrier(
        Vector3 destination,
        float speed,
        NavMeshPath partialPath)
    {
        if (agent == null ||
            itemPusher == null ||
            partialPath == null ||
            partialPath.status != NavMeshPathStatus.PathPartial)
        {
            CancelPushAuthorizationIfIdle();
            return false;
        }

        Vector3[] corners = partialPath.corners;

        if (corners == null || corners.Length < 2)
        {
            CancelPushAuthorizationIfIdle();
            return false;
        }

        Vector3 endpoint = corners[^1];
        Vector3 routeDirection = destination - endpoint;
        routeDirection.y = 0f;

        if (routeDirection.sqrMagnitude < 0.0001f)
        {
            routeDirection = endpoint - corners[^2];
            routeDirection.y = 0f;
        }

        if (!itemPusher.TryFindPushableItemNear(
                endpoint,
                routeDirection,
                out ItemNavigationObstacle pushableItem))
        {
            CancelPushAuthorizationIfIdle();
            return false;
        }

        agent.isStopped = false;
        agent.speed = speed;

        if (!agent.SetPath(partialPath))
        {
            CancelPushAuthorizationIfIdle();
            return false;
        }

        itemPusher.AuthorizePush(pushableItem);
        return true;
    }

    private void CancelPushAuthorizationIfIdle()
    {
        if (itemPusher != null && !itemPusher.IsPushingAnyItem)
        {
            itemPusher.CancelAuthorizedPush();
        }
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
