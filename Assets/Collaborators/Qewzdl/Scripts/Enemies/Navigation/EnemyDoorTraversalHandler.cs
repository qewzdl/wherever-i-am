using System.Collections.Generic;
using UnityEngine;

internal sealed class EnemyDoorTraversalHandler : IEnemyTraversalHandler
{
    private readonly EnemyDoorInteractor interactor;
    private EnemyTraversalPlan currentPlan;

    public EnemyTraversalKind Kind => EnemyTraversalKind.Door;
    public bool IsActive => interactor != null && interactor.HasActiveInteraction;
    public EnemyTraversalPlan CurrentPlan => currentPlan;

    public EnemyDoorTraversalHandler(EnemyDoorInteractor interactor)
    {
        this.interactor = interactor;
    }

    public EnemyDoorNavigationResult Evaluate(
        Vector3 currentPosition,
        Vector3 requestedDestination,
        IReadOnlyList<Vector3> routeCorners = null)
    {
        if (interactor == null)
        {
            currentPlan = default;
            return EnemyDoorNavigationResult.None;
        }

        bool wasActive = interactor.HasActiveInteraction;
        EnemyDoorNavigationResult result = interactor.EvaluateNavigation(
            currentPosition,
            requestedDestination,
            routeCorners);

        if (result.HasOverrideDestination)
        {
            currentPlan = new EnemyTraversalPlan(
                EnemyTraversalKind.Door,
                EnemyTraversalStatus.MovingToEntry,
                result.OverrideDestination,
                result.OverrideDestination);
        }
        else if (result.ShouldStop)
        {
            currentPlan = new EnemyTraversalPlan(
                EnemyTraversalKind.Door,
                EnemyTraversalStatus.Waiting,
                currentPosition,
                currentPosition);
        }
        else
        {
            currentPlan = default;

            if (wasActive)
            {
                EnemyNavigationTopology.MarkChanged();
            }
        }

        return result;
    }

    public void Cancel()
    {
        interactor?.CancelActiveInteraction();
        currentPlan = default;
    }
}
