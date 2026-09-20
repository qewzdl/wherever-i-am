using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public sealed class EnemyPatrolController
{
    private readonly EnemyPatrolRoute patrolRoute;
    private readonly EnemyNavigator navigator;
    private readonly EnemyConfig config;
    private readonly EnemyBlackboard blackboard;
    private readonly EnemyPatrolStopWanderPlanner stopWanderPlanner = new();
    private readonly EnemyPatrolPathPlanner pathPlanner;
    private readonly List<Vector3> plannedRoutePoints = new();

    // Which point she walks to next, and which way round the loop she is
    // going. Meaningless until she has joined the route - before that there is
    // no "next", and joining is what picks both.
    private int nextRoutePointIndex;
    private bool hasJoinedRoute;
    private int routeDirection = 1;

    private int nextPlannedRoutePointIndex;
    private Transform currentRoutePoint;
    private bool hasActiveWanderDestination;

    public bool HasRoute => patrolRoute != null && patrolRoute.HasPoints;
    public bool HasCurrentRoutePoint => currentRoutePoint != null;
    public bool HasActiveWanderDestination => hasActiveWanderDestination;

    public Vector3 CurrentRoutePointPosition =>
        currentRoutePoint != null ? currentRoutePoint.position : Vector3.zero;

    public EnemyPatrolController(
        EnemyPatrolRoute patrolRoute,
        EnemyNavigator navigator,
        EnemyConfig config,
        EnemyBlackboard blackboard = null
    )
    {
        this.patrolRoute = patrolRoute;
        this.navigator = navigator;
        this.config = config;
        this.blackboard = blackboard;

        int plannerSeed = navigator != null ? navigator.GetInstanceID() : 0;
        pathPlanner = new EnemyPatrolPathPlanner(plannerSeed);
    }

    public bool MoveToNextRoutePoint()
    {
        hasActiveWanderDestination = false;
        ClearPlannedRoute();

        if (!HasRoute || navigator == null || config == null)
        {
            currentRoutePoint = null;
            blackboard?.ClearCurrentDestination();
            return false;
        }

        // The first leg of a match used to walk to whichever point the
        // designer happened to drag in first, from wherever she spawned -
        // sometimes across the whole level, and always the same point.
        if (!hasJoinedRoute && !JoinRouteNear(navigator.Position))
        {
            currentRoutePoint = null;
            blackboard?.ClearCurrentDestination();
            return false;
        }

        currentRoutePoint = patrolRoute.GetPoint(nextRoutePointIndex);
        AdvanceRouteIndex();

        if (currentRoutePoint == null)
        {
            blackboard?.ClearCurrentDestination();
            return false;
        }

        BuildPlannedRoute(currentRoutePoint.position);
        bool moved = TryMoveToNextPlannedRoutePoint();

        if (!moved)
        {
            blackboard?.ClearCurrentDestination();
        }

        return moved;
    }

    // Pick the loop up near somewhere, instead of where it was interrupted.
    //
    // Nothing about the route itself is disturbed: same points, same spacing.
    // She simply rejoins it in a different place, which is the whole of it.
    public bool ResumeNearest(Vector3 position)
    {
        return JoinRouteNear(position);
    }

    public bool HasReachedCurrentRoutePoint()
    {
        if (navigator == null || config == null || currentRoutePoint == null)
        {
            return false;
        }

        if (!navigator.HasReached(config.patrolPointReachDistance))
        {
            return false;
        }

        if (nextPlannedRoutePointIndex >= plannedRoutePoints.Count)
        {
            return true;
        }

        TryMoveToNextPlannedRoutePoint();
        return false;
    }

    public bool ShouldUseStopWander()
    {
        return config != null &&
               config.patrolStopDuration > 0f &&
               config.patrolStopWanderRadius > 0f;
    }

    public bool MoveToRandomPointAroundCurrentRoutePoint()
    {
        hasActiveWanderDestination = false;

        if (navigator == null || config == null || currentRoutePoint == null)
        {
            blackboard?.ClearCurrentDestination();
            return false;
        }

        EnemyPosture posture = blackboard != null
            ? blackboard.CurrentPosture
            : EnemyPosture.Standing;

        if (!navigator.TryGetNavigationQueryFilter(
                posture,
                out NavMeshQueryFilter filter) ||
            !stopWanderPlanner.TryGetRandomWanderPoint(
            currentRoutePoint.position,
            navigator.Position,
            config.patrolStopWanderRadius,
            config.patrolStopWanderMinDistanceFromEnemy,
            config.patrolStopWanderSampleAttempts,
            filter,
            out Vector3 wanderPoint
        ))
        {
            blackboard?.ClearCurrentDestination();
            return false;
        }

        hasActiveWanderDestination = navigator.TryMoveTo(
            wanderPoint,
            config.patrolStopWanderSpeed
        );

        if (hasActiveWanderDestination)
        {
            blackboard?.SetCurrentDestination(wanderPoint);
        }
        else
        {
            blackboard?.ClearCurrentDestination();
        }

        return hasActiveWanderDestination;
    }

    public bool HasReachedActiveWanderDestination()
    {
        if (!hasActiveWanderDestination || navigator == null || config == null)
        {
            return false;
        }

        return navigator.HasReached(config.patrolStopWanderPointReachDistance);
    }

    public void ClearActiveWanderDestination()
    {
        hasActiveWanderDestination = false;
    }

    public void Reset()
    {
        hasJoinedRoute = false;
        currentRoutePoint = null;
        hasActiveWanderDestination = false;
        ClearPlannedRoute();
        blackboard?.ClearCurrentDestination();
    }

    // Step round the loop, and now and then turn round instead.
    //
    // The circuit ran one way from one point for the whole match, and a
    // patrol is the thing a player sees most of: two matches was enough to
    // learn where she would be and which way she would be facing, and after
    // that the level had a timetable rather than somebody walking round it.
    // The wander at each stop softened where she stood; it never touched the
    // order or the direction.
    //
    // Rolled per point rather than per lap, so how often she turns does not
    // depend on how many points a particular route happens to have. The turn
    // sends her back to the point she has just come from, which is what
    // somebody doubling back actually does - and is why it wants to stay
    // rare. At a half chance she would spend the match pacing between two
    // points, which is not unpredictable, only broken.
    // Its own copy, because this is the one consumer of crawling that holds no
    // posture controller to ask. Installed by the same module as the other
    // half, so the two cannot disagree about what the enemy can do.
    private bool canCrawl;

    public void InstallCrawling()
    {
        canCrawl = true;
    }

    public void ForgetInstalledCrawling()
    {
        canCrawl = false;
    }

    private void AdvanceRouteIndex()
    {
        if (config != null && Random.value < config.patrolReverseChance)
        {
            routeDirection = -routeDirection;
        }

        nextRoutePointIndex += routeDirection;
    }

    // Joining is the one moment both the place and the direction are open, so
    // both are decided here: the nearest point, and a coin for which way round
    // she sets off. A search that ends near the same doorway twice does not
    // then send her the same way twice.
    private bool JoinRouteNear(Vector3 position)
    {
        if (!HasRoute)
        {
            return false;
        }

        int nearest = patrolRoute.NearestPointIndex(position);

        if (nearest < 0)
        {
            return false;
        }

        nextRoutePointIndex = nearest;
        routeDirection = Random.value < 0.5f ? 1 : -1;
        hasJoinedRoute = true;

        return true;
    }

    private void BuildPlannedRoute(Vector3 destination)
    {
        ClearPlannedRoute();

        if (!EnemyServerPerceptionScheduler.TryReservePathQueries(
                navigator.NavigationSchedulerId,
                config.navigationMaximumPathQueriesPerRepath,
                out int remainingPathQueries))
        {
            // Route variation is optional. Under load the authoritative
            // navigator will still build the direct route when its fair turn
            // arrives; skipping decoration must not send Patrol to Idle.
            plannedRoutePoints.Add(destination);
            return;
        }

        if (TryBuildPlannedRouteForPosture(
                destination,
                EnemyPosture.Standing,
                ref remainingPathQueries))
        {
            return;
        }

        if (canCrawl &&
            TryBuildPlannedRouteForPosture(
                destination,
                EnemyPosture.Crawling,
                ref remainingPathQueries))
        {
            return;
        }

        plannedRoutePoints.Add(destination);
    }

    private bool TryBuildPlannedRouteForPosture(
        Vector3 destination,
        EnemyPosture posture,
        ref int remainingPathQueries
    )
    {
        plannedRoutePoints.Clear();

        return navigator.TryGetNavigationQueryFilter(posture, out NavMeshQueryFilter filter) &&
               pathPlanner.TryBuildPlan(
                   navigator.Position,
                   destination,
                   filter,
                   config,
                   plannedRoutePoints,
                   ref remainingPathQueries
               ) &&
               plannedRoutePoints.Count > 0;
    }

    private bool TryMoveToNextPlannedRoutePoint()
    {
        while (nextPlannedRoutePointIndex < plannedRoutePoints.Count)
        {
            bool isFinalPoint =
                nextPlannedRoutePointIndex == plannedRoutePoints.Count - 1;

            Vector3 destination = plannedRoutePoints[nextPlannedRoutePointIndex];

            if (navigator.TryMoveTo(destination, config.patrolSpeed))
            {
                nextPlannedRoutePointIndex++;
                blackboard?.SetCurrentDestination(destination);
                return true;
            }

            if (isFinalPoint)
            {
                blackboard?.ClearCurrentDestination();
                return false;
            }

            nextPlannedRoutePointIndex++;
        }

        return false;
    }

    private void ClearPlannedRoute()
    {
        plannedRoutePoints.Clear();
        nextPlannedRoutePointIndex = 0;
    }
}
