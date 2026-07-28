using System;
using UnityEngine;

internal sealed class EnemyNavigationRecoveryController
{
    private readonly EnemyNavigationProgressMonitor progressMonitor = new();
    private readonly Action navigationInvalidated;
    private readonly Action stuckRecovered;

    private EnemyNavigationConfig config;
    private bool isTracking;

    public EnemyNavigationRecoveryController(
        Action navigationInvalidated,
        Action stuckRecovered)
    {
        this.navigationInvalidated = navigationInvalidated;
        this.stuckRecovered = stuckRecovered;
    }

    public void Configure(EnemyConfig enemyConfig)
    {
        config = enemyConfig != null
            ? enemyConfig.NavigationProfile
            : null;
        Reset();
    }

    public void EnsureTracking(Vector3 position)
    {
        if (isTracking)
        {
            return;
        }

        Begin(position);
    }

    public void Begin(Vector3 position)
    {
        isTracking = true;
        progressMonitor.Begin(position);
    }

    public bool TryRecover(Vector3 position)
    {
        if (!isTracking || !progressMonitor.IsStuck(position, config))
        {
            return false;
        }

        Reset();
        navigationInvalidated?.Invoke();
        stuckRecovered?.Invoke();
        return true;
    }

    public void Reset()
    {
        isTracking = false;
        progressMonitor.Reset();
    }
}
