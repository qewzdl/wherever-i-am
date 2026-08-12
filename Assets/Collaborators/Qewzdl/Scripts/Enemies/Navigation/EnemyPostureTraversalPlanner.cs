using System;
using UnityEngine;
using UnityEngine.AI;

internal enum EnemyPostureTraversalPlanResult
{
    Found = 0,
    NotFound = 1,
    Deferred = 2
}

internal readonly struct EnemyPostureTraversalLeg
{
    public EnemyPosture Posture { get; }
    public Vector3 Destination { get; }
    public NavMeshPath Path { get; }

    public EnemyPostureTraversalLeg(
        EnemyPosture posture,
        Vector3 destination,
        NavMeshPath path)
    {
        Posture = posture;
        Destination = destination;
        Path = path;
    }
}

internal readonly struct EnemyPostureTraversalPlan
{
    public EnemyPostureTraversalLeg FirstLeg { get; }
    public EnemyPostureTraversalLeg SecondLeg { get; }
    public int LegCount { get; }

    // Runtime navigation applies the first leg and replans at its endpoint.
    // These aliases keep that hot path small while tactical planning can still
    // inspect the complete posture sequence.
    public EnemyPosture Posture => FirstLeg.Posture;
    public Vector3 Destination => FirstLeg.Destination;
    public NavMeshPath Path => FirstLeg.Path;
    public bool IsComplete => LegCount > 0;

    private EnemyPostureTraversalPlan(
        EnemyPostureTraversalLeg firstLeg,
        EnemyPostureTraversalLeg secondLeg,
        int legCount)
    {
        FirstLeg = firstLeg;
        SecondLeg = secondLeg;
        LegCount = legCount;
    }

    public static EnemyPostureTraversalPlan Single(
        EnemyPostureTraversalLeg leg)
    {
        return new EnemyPostureTraversalPlan(leg, default, 1);
    }

    public static EnemyPostureTraversalPlan Sequence(
        EnemyPostureTraversalLeg firstLeg,
        EnemyPostureTraversalLeg secondLeg)
    {
        return new EnemyPostureTraversalPlan(firstLeg, secondLeg, 2);
    }

    public EnemyPostureTraversalLeg GetLeg(int index)
    {
        return index switch
        {
            0 when LegCount > 0 => FirstLeg,
            1 when LegCount > 1 => SecondLeg,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }
}

internal sealed class EnemyPostureTraversalPlanner : IEnemyTraversalHandler
{
    private const float MinimumStandingWaypointDistance = 0.1f;
    private const float StandingWaypointArrivalTolerance = 0.05f;

    private readonly Transform ownerTransform;
    private readonly NavMeshAgent agent;
    private readonly EnemyPostureController postureController;
    private readonly EnemyNavigationQueryService queryService;
    private readonly Func<NavMeshPath, bool> hasNavigationBlocker;

    // A complete posture plan has at most two legs: walk standing to the last
    // useful point before a low passage, then crawl to the requested endpoint.
    // The paths stay owned by this planner and are consumed synchronously.
    private readonly NavMeshPath standingPath = new();
    private readonly NavMeshPath crawlingPath = new();
    private readonly NavMeshPath standingCandidatePath = new();
    private readonly NavMeshPath crawlingContinuationPath = new();

    private EnemyConfig config;
    private StandingWaypointSearch runtimeWaypointSearch;
    private StandingWaypointSearch tacticalWaypointSearch;
    private bool hasPendingRuntimeStandingPrefix;
    private Vector3 pendingRuntimeDestination;
    private Vector3 pendingRuntimeStandingEndpoint;

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
        runtimeWaypointSearch = default;
        tacticalWaypointSearch = default;
        hasPendingRuntimeStandingPrefix = false;
        postureController?.ResetStandingRecoverySchedule();
    }

    // Runtime and tactical preview both enter the same complete route builder.
    // Runtime applies only the first leg because changing posture at the join
    // invalidates the NavMeshAgent path; its next normal repath continues with
    // the second leg.
    public bool TryBuildPlan(
        Vector3 destination,
        out EnemyPostureTraversalPlan plan)
    {
        plan = default;
        LastPlannedPath = null;

        if (!CanPlan())
        {
            return false;
        }

        EnemyPostureTraversalPlanResult pendingResult =
            TryBuildPendingRuntimeStandingPrefix(destination, out plan);

        if (pendingResult == EnemyPostureTraversalPlanResult.Found)
        {
            LastPlannedPath = plan.Path;
            return true;
        }

        if (pendingResult == EnemyPostureTraversalPlanResult.Deferred)
        {
            return false;
        }

        EnemyPosture firstPosture =
            postureController.TryBeginStandingRecoveryCheck()
                ? EnemyPosture.Standing
                : EnemyPosture.Crawling;

        EnemyPostureTraversalPlanResult result = TryBuildCompletePlan(
            destination,
            firstPosture,
            ref runtimeWaypointSearch,
            out plan);

        if (result != EnemyPostureTraversalPlanResult.Found)
        {
            hasPendingRuntimeStandingPrefix = false;
            return false;
        }

        if (plan.LegCount > 1)
        {
            hasPendingRuntimeStandingPrefix = true;
            pendingRuntimeDestination = destination;
            pendingRuntimeStandingEndpoint = plan.FirstLeg.Destination;
        }
        else
        {
            hasPendingRuntimeStandingPrefix = false;
        }

        LastPlannedPath = plan.Path;
        return true;
    }

    // Side-effect-free preview of the same complete posture sequence. Its
    // allowance was already reserved by the server scheduler, so it is copied
    // into the shared query service and the actual number of path calculations
    // is returned to the caller. A long standing-waypoint search resumes from
    // its saved sample instead of starting over every frame.
    internal bool TryBuildTacticalPlan(
        Vector3 destination,
        ref int pathBudget,
        out EnemyPostureTraversalPlan plan,
        out bool budgetExhausted)
    {
        plan = default;
        budgetExhausted = false;

        if (!CanPlan())
        {
            tacticalWaypointSearch = default;
            return false;
        }

        if (pathBudget <= 0)
        {
            budgetExhausted = true;
            return false;
        }

        queryService.BeginRepath(pathBudget);

        EnemyPostureTraversalPlanResult result = TryBuildCompletePlan(
            destination,
            postureController.GetPreferredPlanningPosture(),
            ref tacticalWaypointSearch,
            out plan);

        pathBudget = queryService.RemainingPathQueries;
        budgetExhausted = result == EnemyPostureTraversalPlanResult.Deferred;
        return result == EnemyPostureTraversalPlanResult.Found;
    }

    public void NotifyPostureChanged(EnemyPosture posture)
    {
        runtimeWaypointSearch = default;
        tacticalWaypointSearch = default;

        if (posture != EnemyPosture.Standing)
        {
            hasPendingRuntimeStandingPrefix = false;
        }

        postureController?.DelayStandingRecoveryAfterPostureChange(posture);
    }

    public void Cancel()
    {
        LastPlannedPath = null;
        runtimeWaypointSearch = default;
        tacticalWaypointSearch = default;
        hasPendingRuntimeStandingPrefix = false;
    }

    private EnemyPostureTraversalPlanResult
        TryBuildPendingRuntimeStandingPrefix(
            Vector3 destination,
            out EnemyPostureTraversalPlan plan)
    {
        plan = default;

        if (!hasPendingRuntimeStandingPrefix)
        {
            return EnemyPostureTraversalPlanResult.NotFound;
        }

        float destinationTolerance = Mathf.Max(
            0.1f,
            config.navigationDestinationRepathDistance);
        Vector3 destinationDelta = destination - pendingRuntimeDestination;
        destinationDelta.y = 0f;

        if (destinationDelta.sqrMagnitude >
            destinationTolerance * destinationTolerance)
        {
            hasPendingRuntimeStandingPrefix = false;
            return EnemyPostureTraversalPlanResult.NotFound;
        }

        if (postureController.CurrentPosture != EnemyPosture.Standing)
        {
            hasPendingRuntimeStandingPrefix = false;
            return EnemyPostureTraversalPlanResult.NotFound;
        }

        float arrivalDistance = Mathf.Max(
            agent.stoppingDistance,
            MinimumStandingWaypointDistance) +
            StandingWaypointArrivalTolerance;
        Vector3 waypointDelta =
            pendingRuntimeStandingEndpoint - ownerTransform.position;
        waypointDelta.y = 0f;

        if (waypointDelta.sqrMagnitude <= arrivalDistance * arrivalDistance)
        {
            hasPendingRuntimeStandingPrefix = false;
            return EnemyPostureTraversalPlanResult.NotFound;
        }

        EnemyPostureTraversalPlanResult result = TryBuildLeg(
            ownerTransform.position,
            pendingRuntimeStandingEndpoint,
            EnemyPosture.Standing,
            standingPath,
            out EnemyPostureTraversalLeg standingLeg);

        if (result == EnemyPostureTraversalPlanResult.Found)
        {
            plan = EnemyPostureTraversalPlan.Single(standingLeg);
            return result;
        }

        if (result == EnemyPostureTraversalPlanResult.NotFound)
        {
            hasPendingRuntimeStandingPrefix = false;
        }

        return result;
    }

    private EnemyPostureTraversalPlanResult TryBuildCompletePlan(
        Vector3 destination,
        EnemyPosture firstPosture,
        ref StandingWaypointSearch waypointSearch,
        out EnemyPostureTraversalPlan plan)
    {
        plan = default;

        EnemyPostureTraversalPlanResult directResult = TryBuildLeg(
            ownerTransform.position,
            destination,
            firstPosture,
            firstPosture == EnemyPosture.Standing
                ? standingPath
                : crawlingPath,
            out EnemyPostureTraversalLeg directLeg);

        if (directResult == EnemyPostureTraversalPlanResult.Deferred)
        {
            return directResult;
        }

        if (directResult == EnemyPostureTraversalPlanResult.Found)
        {
            waypointSearch = default;
            plan = EnemyPostureTraversalPlan.Single(directLeg);
            return EnemyPostureTraversalPlanResult.Found;
        }

        if (firstPosture == EnemyPosture.Crawling ||
            config == null ||
            !config.crawlingEnabled)
        {
            waypointSearch = default;
            return EnemyPostureTraversalPlanResult.NotFound;
        }

        EnemyPostureTraversalPlanResult crawlResult = TryBuildLeg(
            ownerTransform.position,
            destination,
            EnemyPosture.Crawling,
            crawlingPath,
            out EnemyPostureTraversalLeg crawlLeg);

        if (crawlResult != EnemyPostureTraversalPlanResult.Found)
        {
            if (crawlResult == EnemyPostureTraversalPlanResult.NotFound)
            {
                waypointSearch = default;
            }

            return crawlResult;
        }

        // An enemy already standing simply changes posture at its feet. The
        // intermediate standing leg is only a recovery optimisation for an
        // enemy that is currently crawling but is allowed to get up again.
        if (!postureController.IsCrawling)
        {
            waypointSearch = default;
            plan = EnemyPostureTraversalPlan.Single(crawlLeg);
            return EnemyPostureTraversalPlanResult.Found;
        }

        EnemyPostureTraversalPlanResult waypointResult =
            TryBuildStandingWaypointSequence(
                destination,
                crawlLeg,
                ref waypointSearch,
                out plan);

        if (waypointResult == EnemyPostureTraversalPlanResult.NotFound)
        {
            // No useful standing prefix exists. The complete crawl route that
            // was already calculated is still the route runtime will use.
            plan = EnemyPostureTraversalPlan.Single(crawlLeg);
            return EnemyPostureTraversalPlanResult.Found;
        }

        return waypointResult;
    }

    private EnemyPostureTraversalPlanResult TryBuildStandingWaypointSequence(
        Vector3 finalDestination,
        EnemyPostureTraversalLeg crawlLeg,
        ref StandingWaypointSearch search,
        out EnemyPostureTraversalPlan plan)
    {
        plan = default;
        Vector3[] corners = crawlLeg.Path.corners;

        if (corners == null || corners.Length < 2)
        {
            search = default;
            return EnemyPostureTraversalPlanResult.NotFound;
        }

        int fingerprint = Fingerprint(corners, crawlLeg.Destination);

        if (!search.Active || search.Fingerprint != fingerprint)
        {
            search = new StandingWaypointSearch(
                fingerprint,
                corners.Length - 2,
                -1);
        }

        float sampleStep = Mathf.Max(0.1f, config.postureNavMeshSampleRadius);
        float minimumDistance = Mathf.Max(
            agent.stoppingDistance,
            MinimumStandingWaypointDistance) +
            StandingWaypointArrivalTolerance;
        float minimumSqrDistance = minimumDistance * minimumDistance;

        for (int segmentIndex = search.Segment;
             segmentIndex >= 0;
             segmentIndex--)
        {
            Vector3 segmentStart = corners[segmentIndex];
            Vector3 segmentEnd = corners[segmentIndex + 1];
            float segmentLength = Vector3.Distance(segmentStart, segmentEnd);
            int sampleCount = Mathf.Max(
                1,
                Mathf.CeilToInt(segmentLength / sampleStep));
            int firstSample = segmentIndex == search.Segment && search.Sample >= 0
                ? Mathf.Min(search.Sample, sampleCount)
                : sampleCount;

            for (int sampleIndex = firstSample;
                 sampleIndex >= 0;
                 sampleIndex--)
            {
                Vector3 candidate = Vector3.Lerp(
                    segmentStart,
                    segmentEnd,
                    sampleIndex / (float)sampleCount);
                Vector3 delta = candidate - ownerTransform.position;
                delta.y = 0f;

                if (delta.sqrMagnitude <= minimumSqrDistance)
                {
                    continue;
                }

                EnemyPostureTraversalPlanResult standingResult = TryBuildLeg(
                    ownerTransform.position,
                    candidate,
                    EnemyPosture.Standing,
                    standingCandidatePath,
                    out EnemyPostureTraversalLeg standingLeg);

                if (standingResult == EnemyPostureTraversalPlanResult.Deferred)
                {
                    search = new StandingWaypointSearch(
                        fingerprint,
                        segmentIndex,
                        sampleIndex);
                    return standingResult;
                }

                if (standingResult != EnemyPostureTraversalPlanResult.Found)
                {
                    continue;
                }

                // Sampling can pull a point from inside the low passage back
                // onto the standing edge. Judge the resolved endpoint too: a
                // nominally distant candidate whose actual path ends inside
                // stopping distance produces a zero-length standing leg and
                // makes the enemy alternate postures at the doorway forever.
                Vector3 resolvedDelta =
                    standingLeg.Destination - ownerTransform.position;
                resolvedDelta.y = 0f;

                if (resolvedDelta.sqrMagnitude <= minimumSqrDistance)
                {
                    continue;
                }

                EnemyPostureTraversalPlanResult continuationResult =
                    TryBuildLeg(
                        standingLeg.Destination,
                        finalDestination,
                        EnemyPosture.Crawling,
                        crawlingContinuationPath,
                        out EnemyPostureTraversalLeg continuationLeg);

                if (continuationResult == EnemyPostureTraversalPlanResult.Deferred)
                {
                    search = new StandingWaypointSearch(
                        fingerprint,
                        segmentIndex,
                        sampleIndex);
                    return continuationResult;
                }

                if (continuationResult != EnemyPostureTraversalPlanResult.Found)
                {
                    continue;
                }

                search = default;
                plan = EnemyPostureTraversalPlan.Sequence(
                    standingLeg,
                    continuationLeg);
                return EnemyPostureTraversalPlanResult.Found;
            }
        }

        search = default;
        return EnemyPostureTraversalPlanResult.NotFound;
    }

    private EnemyPostureTraversalPlanResult TryBuildLeg(
        Vector3 source,
        Vector3 destination,
        EnemyPosture posture,
        NavMeshPath path,
        out EnemyPostureTraversalLeg leg)
    {
        leg = default;
        path.ClearCorners();

        if (!TryBuildPathForPosture(
                source,
                destination,
                posture,
                path,
                out Vector3 endpoint))
        {
            return queryService.WasPathBudgetExhausted
                ? EnemyPostureTraversalPlanResult.Deferred
                : EnemyPostureTraversalPlanResult.NotFound;
        }

        bool blocked = hasNavigationBlocker != null &&
                       hasNavigationBlocker(path);

        if (path.status != NavMeshPathStatus.PathComplete || blocked)
        {
            return EnemyPostureTraversalPlanResult.NotFound;
        }

        leg = new EnemyPostureTraversalLeg(posture, endpoint, path);
        return EnemyPostureTraversalPlanResult.Found;
    }

    private bool TryBuildPathForPosture(
        Vector3 source,
        Vector3 destination,
        EnemyPosture posture,
        NavMeshPath path,
        out Vector3 endpoint)
    {
        endpoint = destination;
        float sourceSampleRadius = Mathf.Max(
            0.05f,
            config.postureSwitchSampleRadius);
        NavMeshHit sourceHit;
        Vector3 sourceDelta = source - ownerTransform.position;
        sourceDelta.y = 0f;
        bool startsAtOwner = sourceDelta.sqrMagnitude <= 0.0025f;

        if (startsAtOwner)
        {
            if (!postureController.CanUsePostureAtCurrentPosition(posture))
            {
                path.ClearCorners();
                return false;
            }

            NavMeshQueryFilter filter = BuildFilter(posture);

            if (!queryService.TrySamplePosition(
                    ownerTransform.position,
                    sourceSampleRadius,
                    filter,
                    out sourceHit))
            {
                path.ClearCorners();
                return false;
            }
        }
        else if (!postureController.TryGetUsablePosturePosition(
                     posture,
                     source,
                     sourceSampleRadius,
                     out sourceHit))
        {
            path.ClearCorners();
            return false;
        }

        // postureSwitchSampleRadius is a tight tolerance for changing posture
        // at the enemy's feet. A moving destination near a carved boundary
        // needs the normal navigation landing radius instead.
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

        endpoint = destinationHit.position;

        return queryService.TryCalculatePath(
            sourceHit.position,
            destinationHit.position,
            BuildFilter(posture),
            path);
    }

    private NavMeshQueryFilter BuildFilter(EnemyPosture posture)
    {
        return new NavMeshQueryFilter
        {
            agentTypeID = postureController.GetAgentTypeIdForPosture(posture),
            areaMask = agent.areaMask
        };
    }

    private bool CanPlan()
    {
        return config != null &&
               agent != null &&
               postureController != null &&
               queryService != null;
    }

    private static int Fingerprint(
        Vector3[] corners,
        Vector3 destination)
    {
        unchecked
        {
            int hash = corners.Length;

            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 corner = corners[i];
                hash = hash * 31 + Mathf.RoundToInt(corner.x * 4f);
                hash = hash * 31 + Mathf.RoundToInt(corner.y * 4f);
                hash = hash * 31 + Mathf.RoundToInt(corner.z * 4f);
            }

            hash = hash * 31 + Mathf.RoundToInt(destination.x * 4f);
            hash = hash * 31 + Mathf.RoundToInt(destination.y * 4f);
            hash = hash * 31 + Mathf.RoundToInt(destination.z * 4f);
            return hash;
        }
    }

    private readonly struct StandingWaypointSearch
    {
        public bool Active { get; }
        public int Fingerprint { get; }
        public int Segment { get; }
        public int Sample { get; }

        public StandingWaypointSearch(
            int fingerprint,
            int segment,
            int sample)
        {
            Active = true;
            Fingerprint = fingerprint;
            Segment = segment;
            Sample = sample;
        }
    }
}
