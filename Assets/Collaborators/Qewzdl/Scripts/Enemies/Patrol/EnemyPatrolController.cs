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

    private int patrolPointIndex;
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

        currentRoutePoint = patrolRoute.GetPoint(patrolPointIndex);
        patrolPointIndex++;

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

        if (!stopWanderPlanner.TryGetRandomWanderPoint(
            currentRoutePoint.position,
            navigator.Position,
            config.patrolStopWanderRadius,
            config.patrolStopWanderMinDistanceFromEnemy,
            config.patrolStopWanderSampleAttempts,
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
        patrolPointIndex = 0;
        currentRoutePoint = null;
        hasActiveWanderDestination = false;
        ClearPlannedRoute();
        blackboard?.ClearCurrentDestination();
    }

    private void BuildPlannedRoute(Vector3 destination)
    {
        ClearPlannedRoute();

        if (TryBuildPlannedRouteForPosture(destination, EnemyPosture.Standing))
        {
            return;
        }

        if (config.crawlingEnabled &&
            TryBuildPlannedRouteForPosture(destination, EnemyPosture.Crawling))
        {
            return;
        }

        plannedRoutePoints.Add(destination);
    }

    private bool TryBuildPlannedRouteForPosture(
        Vector3 destination,
        EnemyPosture posture
    )
    {
        plannedRoutePoints.Clear();

        return navigator.TryGetNavigationQueryFilter(posture, out NavMeshQueryFilter filter) &&
               pathPlanner.TryBuildPlan(
                   navigator.Position,
                   destination,
                   filter,
                   config,
                   plannedRoutePoints
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
