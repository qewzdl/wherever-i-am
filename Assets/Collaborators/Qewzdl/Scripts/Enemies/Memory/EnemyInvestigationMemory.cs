using System.Collections.Generic;
using UnityEngine;

public sealed class EnemyInvestigationMemory
{
    private readonly List<EnemyInvestigationSearchPoint> currentInvestigationRoute = new();

    public Vector3 LastKnownTargetPosition { get; private set; }
    public bool HasLastKnownTargetPosition { get; private set; }

    public Vector3 SuspiciousPosition { get; private set; }
    public bool HasSuspiciousPosition { get; private set; }

    public IReadOnlyList<EnemyInvestigationSearchPoint> CurrentInvestigationRoute => currentInvestigationRoute;

    public int ActiveSearchRouteIndex { get; private set; } = -1;
    public bool HasActiveSearchRouteIndex => ActiveSearchRouteIndex >= 0;

    public void RememberLastKnownTargetPosition(Vector3 position)
    {
        LastKnownTargetPosition = position;
        HasLastKnownTargetPosition = true;
    }

    public bool TryGetLastKnownTargetPosition(out Vector3 position)
    {
        position = LastKnownTargetPosition;
        return HasLastKnownTargetPosition;
    }

    public void ClearLastKnownTargetPosition()
    {
        LastKnownTargetPosition = default;
        HasLastKnownTargetPosition = false;
    }

    public void RememberSuspiciousPosition(Vector3 position)
    {
        SuspiciousPosition = position;
        HasSuspiciousPosition = true;
    }

    public bool TryGetSuspiciousPosition(out Vector3 position)
    {
        position = SuspiciousPosition;
        return HasSuspiciousPosition;
    }

    public bool PromoteSuspiciousPositionToLastKnown()
    {
        if (!HasSuspiciousPosition)
        {
            return false;
        }

        RememberLastKnownTargetPosition(SuspiciousPosition);
        ClearSuspiciousPosition();

        return true;
    }

    public void ClearSuspiciousPosition()
    {
        SuspiciousPosition = default;
        HasSuspiciousPosition = false;
    }

    public void SetCurrentInvestigationRoute(IReadOnlyList<EnemyInvestigationSearchPoint> route)
    {
        currentInvestigationRoute.Clear();
        ActiveSearchRouteIndex = -1;

        if (route == null)
        {
            return;
        }

        for (int i = 0; i < route.Count; i++)
        {
            currentInvestigationRoute.Add(route[i]);
        }
    }

    public void SetActiveSearchRouteIndex(int routeIndex)
    {
        if (routeIndex < 0 || routeIndex >= currentInvestigationRoute.Count)
        {
            ActiveSearchRouteIndex = -1;
            return;
        }

        ActiveSearchRouteIndex = routeIndex;
    }

    public void ClearCurrentInvestigationRoute()
    {
        currentInvestigationRoute.Clear();
        ActiveSearchRouteIndex = -1;
    }

    public void ClearAll()
    {
        ClearLastKnownTargetPosition();
        ClearSuspiciousPosition();
        ClearCurrentInvestigationRoute();
    }
}