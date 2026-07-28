using System;
using UnityEngine;
using UnityEngine.AI;

internal readonly struct EnemyPostureTraversalPlan
{
    public EnemyPosture Posture { get; }
    public Vector3 Destination { get; }
    public NavMeshPath Path { get; }
    public bool IsComplete { get; }
    public bool IsBlockedByItem { get; }

    public EnemyPostureTraversalPlan(
        EnemyPosture posture,
        Vector3 destination,
        NavMeshPath path,
        bool isComplete,
        bool isBlockedByItem)
    {
        Posture = posture;
        Destination = destination;
        Path = path;
        IsComplete = isComplete;
        IsBlockedByItem = isBlockedByItem;
    }
}

internal sealed class EnemyPostureTraversalPlanner : IEnemyTraversalHandler
{
    private const float MinimumStandingWaypointDistance = 0.1f;

    private readonly Transform ownerTransform;
    private readonly NavMeshAgent agent;
    private readonly EnemyPostureController postureController;
    private readonly EnemyNavigationQueryService queryService;
    private readonly Func<NavMeshPath, bool> hasNavigationBlocker;
    private readonly NavMeshPath standingPath = new();
    private readonly NavMeshPath crawlingPath = new();
    private readonly NavMeshPath standingCandidatePath = new();

    private EnemyConfig config;
    private float nextStandingRecoveryCheckTime;

    public EnemyTraversalKind Kind => EnemyTraversalKind.Posture;
    public bool IsActive => postureController != null &&
                            postureController.IsPostureTransitionInProgress;
    public NavMeshPath LastPlannedPath { get; private set; }

    public EnemyPostureTraversalPlanner(
        Transform ownerTransform,
        NavMeshAgent agent,
        EnemyPostureController postureController,
        EnemyNavigationQueryService queryService,
        Func<NavMeshPath, bool> hasNavigationBlocker)
    {
        this.ownerTransform = ownerTransform;
        this.agent = agent;
        this.postureController = postureController;
        this.queryService = queryService;
        this.hasNavigationBlocker = hasNavigationBlocker;
    }

    public void Configure(EnemyConfig enemyConfig)
    {
        config = enemyConfig;
        nextStandingRecoveryCheckTime = 0f;
    }

    public bool TryBuildPlan(
        Vector3 destination,
        out EnemyPostureTraversalPlan plan)
    {
        plan = default;
        LastPlannedPath = null;

        if (config == null ||
            agent == null ||
            postureController == null ||
            queryService == null)
        {
            return false;
        }

        bool canTryStanding =
            !postureController.IsCrawling || CanAttemptStandingRecovery();
        EnemyPostureTraversalPlan standingFallback = default;
        bool hasStandingFallback = false;

        if (canTryStanding)
        {
            BuildPlanForPosture(
                destination,
                EnemyPosture.Standing,
                standingPath,
                out EnemyPostureTraversalPlan standingPlan);

            if (standingPlan.IsComplete || standingPlan.IsBlockedByItem)
            {
                plan = standingPlan;
                LastPlannedPath = plan.Path;
                return true;
            }

            standingFallback = standingPlan;
            hasStandingFallback = standingPlan.Path != null;

            if (postureController.IsCrawling &&
                TryBuildStandingWaypointPlan(destination, out plan))
            {
                LastPlannedPath = plan.Path;
                return true;
            }
        }

        BuildPlanForPosture(
            destination,
            EnemyPosture.Crawling,
            crawlingPath,
            out plan);

        if (!plan.IsComplete &&
            !plan.IsBlockedByItem &&
            hasStandingFallback &&
            postureController.CurrentPosture == EnemyPosture.Standing)
        {
            plan = standingFallback;
        }

        LastPlannedPath = plan.Path;
        return plan.Path != null;
    }

    public void NotifyPostureChanged(EnemyPosture posture)
    {
        if (config == null)
        {
            nextStandingRecoveryCheckTime = 0f;
            return;
        }

        float now = Time.time;

        if (posture == EnemyPosture.Crawling)
        {
            nextStandingRecoveryCheckTime =
                now + Mathf.Max(0f, config.minPostureDuration);
            return;
        }

        nextStandingRecoveryCheckTime = 0f;
    }

    public void Cancel()
    {
        LastPlannedPath = null;
    }

    private void BuildPlanForPosture(
        Vector3 destination,
        EnemyPosture posture,
        NavMeshPath path,
        out EnemyPostureTraversalPlan plan)
    {
        path.ClearCorners();

        if (!TryBuildPathForPosture(destination, posture, path))
        {
            plan = new EnemyPostureTraversalPlan(
                posture,
                destination,
                path,
                false,
                false);
            return;
        }

        bool blocked = hasNavigationBlocker != null &&
                       hasNavigationBlocker(path);
        plan = new EnemyPostureTraversalPlan(
            posture,
            destination,
            path,
            path.status == NavMeshPathStatus.PathComplete && !blocked,
            path.status == NavMeshPathStatus.PathComplete && blocked);
    }

    private bool TryBuildStandingWaypointPlan(
        Vector3 destination,
        out EnemyPostureTraversalPlan plan)
    {
        plan = default;

        if (!TryBuildPathForPosture(
                destination,
                EnemyPosture.Crawling,
                crawlingPath) ||
            crawlingPath.status != NavMeshPathStatus.PathComplete ||
            hasNavigationBlocker != null && hasNavigationBlocker(crawlingPath))
        {
            return false;
        }

        Vector3[] corners = crawlingPath.corners;

        if (corners == null || corners.Length < 2)
        {
            return false;
        }

        float sampleStep = Mathf.Max(
            0.1f,
            config.postureNavMeshSampleRadius);
        float minimumDistance = Mathf.Max(
            agent.stoppingDistance,
            MinimumStandingWaypointDistance);
        float minimumSqrDistance = minimumDistance * minimumDistance;

        for (int segmentIndex = corners.Length - 2;
             segmentIndex >= 0;
             segmentIndex--)
        {
            Vector3 segmentStart = corners[segmentIndex];
            Vector3 segmentEnd = corners[segmentIndex + 1];
            float segmentLength = Vector3.Distance(
                segmentStart,
                segmentEnd);
            int sampleCount = Mathf.Max(
                1,
                Mathf.CeilToInt(segmentLength / sampleStep));

            for (int sampleIndex = sampleCount;
                 sampleIndex >= 0;
                 sampleIndex--)
            {
                float t = sampleIndex / (float)sampleCount;
                Vector3 candidate = Vector3.Lerp(
                    segmentStart,
                    segmentEnd,
                    t);
                Vector3 delta = candidate - ownerTransform.position;
                delta.y = 0f;

                if (delta.sqrMagnitude <= minimumSqrDistance ||
                    !TryBuildPathForPosture(
                        candidate,
                        EnemyPosture.Standing,
                        standingCandidatePath) ||
                    standingCandidatePath.status !=
                    NavMeshPathStatus.PathComplete ||
                    hasNavigationBlocker != null &&
                    hasNavigationBlocker(standingCandidatePath))
                {
                    continue;
                }

                plan = new EnemyPostureTraversalPlan(
                    EnemyPosture.Standing,
                    candidate,
                    standingCandidatePath,
                    true,
                    false);
                return true;
            }
        }

        return false;
    }

    private bool TryBuildPathForPosture(
        Vector3 destination,
        EnemyPosture posture,
        NavMeshPath path)
    {
        if (!postureController.CanUsePostureAtCurrentPosition(posture))
        {
            path.ClearCorners();
            return false;
        }

        NavMeshQueryFilter filter = new()
        {
            agentTypeID = postureController.GetAgentTypeIdForPosture(posture),
            areaMask = agent.areaMask
        };
        float sourceSampleRadius = Mathf.Max(
            0.05f,
            config.postureSwitchSampleRadius);

        if (!queryService.TrySamplePosition(
                ownerTransform.position,
                sourceSampleRadius,
                filter,
                out NavMeshHit sourceHit))
        {
            path.ClearCorners();
            return false;
        }

        // postureSwitchSampleRadius is a tight tolerance meant for checking
        // whether the enemy itself can physically change posture at its own
        // feet. The destination here is the chase target's live, constantly
        // moving position — sampling it with that same tight radius misses
        // intermittently near any obstacle-carved NavMesh boundary, which
        // wrongly convinced the planner standing was impossible and forced
        // a real (multi-second) crawl transition on every miss.
        float destinationSampleRadius =
            Mathf.Max(0.1f, config.postureNavMeshSampleRadius);

        if (!postureController.TryGetUsablePosturePosition(
                posture,
                destination,
                destinationSampleRadius,
                out NavMeshHit destinationHit))
        {
            path.ClearCorners();
            return false;
        }

        return queryService.TryCalculatePath(
            sourceHit.position,
            destinationHit.position,
            filter,
            path);
    }

    private bool CanAttemptStandingRecovery()
    {
        if (postureController.IsPostureTransitionInProgress)
        {
            return false;
        }

        if (!postureController.IsCrawling)
        {
            return true;
        }

        float now = Time.time;
        float nextAllowedByDuration =
            postureController.LastPostureChangedTime +
            Mathf.Max(0f, config.minPostureDuration);

        if (now < nextAllowedByDuration)
        {
            nextStandingRecoveryCheckTime = Mathf.Max(
                nextStandingRecoveryCheckTime,
                nextAllowedByDuration);
            return false;
        }

        if (now < nextStandingRecoveryCheckTime)
        {
            return false;
        }

        nextStandingRecoveryCheckTime = now + Mathf.Max(
            0.05f,
            config.standingRecoveryCheckInterval);
        return true;
    }
}
