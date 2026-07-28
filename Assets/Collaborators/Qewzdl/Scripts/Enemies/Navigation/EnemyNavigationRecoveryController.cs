using System;
using UnityEngine;

internal sealed class EnemyNavigationRecoveryController
{
    private readonly EnemyNavigationProgressMonitor progressMonitor = new();
    private readonly Action navigationInvalidated;
    private readonly Action stuckRecovered;

    private EnemyNavigationConfig config;
    private bool isTracking;
    private bool usesDirectMovementTimeout;

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

    public void EnsureTracking(
        Vector3 position,
        bool useDirectMovementTimeout)
    {
        if (isTracking &&
            usesDirectMovementTimeout == useDirectMovementTimeout)
        {
            return;
        }

        Begin(position, useDirectMovementTimeout);
    }

    public void Begin(
        Vector3 position,
        bool useDirectMovementTimeout)
    {
        isTracking = true;
        usesDirectMovementTimeout = useDirectMovementTimeout;
        progressMonitor.Begin(position);
    }

    public bool TryRecover(Vector3 position)
    {
        if (!isTracking ||
            !progressMonitor.IsStuck(
                position,
                config,
                usesDirectMovementTimeout))
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
        usesDirectMovementTimeout = false;
        progressMonitor.Reset();
    }
}
