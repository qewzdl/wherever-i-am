using System.Collections.Generic;
using UnityEngine;

public sealed class EnemyInvestigationDebugData
{
    private readonly List<EnemyInvestigationSearchPoint> searchPoints = new();

    public IReadOnlyList<EnemyInvestigationSearchPoint> SearchPoints => searchPoints;

    public bool IsActive { get; private set; }
    public bool HasOrigin { get; private set; }
    public Vector3 Origin { get; private set; }

    public bool HasCurrentDestination { get; private set; }
    public Vector3 CurrentDestination { get; private set; }

    public int ActiveRouteIndex { get; private set; } = -1;

    public void Begin(Vector3 origin)
    {
        Clear();

        IsActive = true;
        HasOrigin = true;
        Origin = origin;
    }

    public void SetSearchPoints(IReadOnlyList<EnemyInvestigationSearchPoint> points)
    {
        searchPoints.Clear();

        if (points == null)
        {
            return;
        }

        for (int i = 0; i < points.Count; i++)
        {
            searchPoints.Add(points[i]);
        }
    }

    public void SetActiveRouteIndex(int routeIndex)
    {
        ActiveRouteIndex = routeIndex;
    }

    public void SetCurrentDestination(Vector3 destination)
    {
        CurrentDestination = destination;
        HasCurrentDestination = true;
    }

    public void ClearCurrentDestination()
    {
        CurrentDestination = default;
        HasCurrentDestination = false;
    }

    public void Finish()
    {
        IsActive = false;
        ActiveRouteIndex = -1;
        ClearCurrentDestination();
    }

    public void Clear()
    {
        searchPoints.Clear();

        IsActive = false;
        HasOrigin = false;
        Origin = default;

        ActiveRouteIndex = -1;
        ClearCurrentDestination();
    }
}