using UnityEngine;

public sealed class EnemyPatrolController
{
    private readonly EnemyPatrolRoute patrolRoute;
    private readonly EnemyNavigator navigator;
    private readonly EnemyConfig config;

    private int patrolPointIndex;

    public bool HasRoute => patrolRoute != null && patrolRoute.HasPoints;

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

    public bool HasReachedCurrentPoint()
    {
        if (navigator == null || config == null)
        {
            return false;
        }

        return navigator.HasReached(config.patrolPointReachDistance);
    }

    public bool MoveToNextPoint()
    {
        if (!HasRoute || navigator == null || config == null)
        {
            return false;
        }

        Transform point = patrolRoute.GetPoint(patrolPointIndex);
        patrolPointIndex++;

        if (point == null)
        {
            return false;
        }

        return navigator.TryMoveTo(point.position, config.patrolSpeed);
    }

    public void Reset()
    {
        patrolPointIndex = 0;
    }
}