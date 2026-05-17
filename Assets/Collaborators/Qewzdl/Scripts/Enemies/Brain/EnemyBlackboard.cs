using System.Collections.Generic;
using UnityEngine;

public sealed class EnemyBlackboard
{
    public EnemyTargetMemory TargetMemory { get; } = new();
    public EnemyPerceptionMemory PerceptionMemory { get; } = new();
    public EnemyInvestigationMemory InvestigationMemory { get; } = new();

    public EnemyInvestigationDebugData InvestigationDebugData { get; } = new();

    public EnemyTarget CurrentTarget => TargetMemory.CurrentTarget;

    public EnemyPerceptionStimulus CurrentStimulus => PerceptionMemory.CurrentStimulus;

    public EnemyPosture CurrentPosture { get; private set; } = EnemyPosture.Standing;

    public Vector3 CurrentDestination { get; private set; }
    public bool HasCurrentDestination { get; private set; }

    public Vector3 SuspiciousPosition => InvestigationMemory.SuspiciousPosition;
    public bool HasSuspiciousPosition => InvestigationMemory.HasSuspiciousPosition;

    public Vector3 LastKnownTargetPosition => InvestigationMemory.LastKnownTargetPosition;
    public bool HasLastKnownTargetPosition => InvestigationMemory.HasLastKnownTargetPosition;

    public float LastVisibleTime => PerceptionMemory.LastVisibleTime;
    public float LastHeardTime => PerceptionMemory.LastHeardTime;

    public IReadOnlyList<EnemyInvestigationSearchPoint> CurrentInvestigationRoute =>
        InvestigationMemory.CurrentInvestigationRoute;

    public void SetCurrentStimulus(EnemyPerceptionStimulus stimulus, float serverTime)
    {
        PerceptionMemory.SetCurrentStimulus(stimulus, serverTime);
    }

    public void ClearCurrentStimulus()
    {
        PerceptionMemory.ClearCurrentStimulus();
    }

    public void SetCurrentPosture(EnemyPosture posture)
    {
        CurrentPosture = posture;
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

    public void SetCurrentInvestigationRoute(IReadOnlyList<EnemyInvestigationSearchPoint> route)
    {
        InvestigationMemory.SetCurrentInvestigationRoute(route);
    }

    public void SetActiveInvestigationRouteIndex(int routeIndex)
    {
        InvestigationMemory.SetActiveSearchRouteIndex(routeIndex);
    }

    public void ClearCurrentInvestigationRoute()
    {
        InvestigationMemory.ClearCurrentInvestigationRoute();
    }

    public void ClearTargetMemory()
    {
        TargetMemory.ClearAll();
    }

    public void ClearAll()
    {
        ClearCurrentDestination();

        CurrentPosture = EnemyPosture.Standing;

        TargetMemory.ClearAll();
        PerceptionMemory.ClearAll();
        InvestigationMemory.ClearAll();
        InvestigationDebugData.Clear();
    }
}