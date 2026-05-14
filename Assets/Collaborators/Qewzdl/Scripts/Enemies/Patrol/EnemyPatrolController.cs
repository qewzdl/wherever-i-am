using UnityEngine;

public sealed class EnemyPatrolController
{
    private readonly EnemyPatrolRoute patrolRoute;
    private readonly EnemyNavigator navigator;
    private readonly EnemyConfig config;
    private readonly EnemyPatrolStopWanderPlanner stopWanderPlanner = new();

    private int patrolPointIndex;
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
        EnemyConfig config
    )
    {
        this.patrolRoute = patrolRoute;
        this.navigator = navigator;
        this.config = config;
    }

    public bool MoveToNextRoutePoint()
    {
        hasActiveWanderDestination = false;

        if (!HasRoute || navigator == null || config == null)
        {
            currentRoutePoint = null;
            return false;
        }

        currentRoutePoint = patrolRoute.GetPoint(patrolPointIndex);
        patrolPointIndex++;

        if (currentRoutePoint == null)
        {
            return false;
        }

        return navigator.TryMoveTo(currentRoutePoint.position, config.patrolSpeed);
    }

    public bool HasReachedCurrentRoutePoint()
    {
        if (navigator == null || config == null || currentRoutePoint == null)
        {
            return false;
        }

        return navigator.HasReached(config.patrolPointReachDistance);
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
            return false;
        }

        hasActiveWanderDestination = navigator.TryMoveTo(
            wanderPoint,
            config.patrolStopWanderSpeed
        );

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
    }
}