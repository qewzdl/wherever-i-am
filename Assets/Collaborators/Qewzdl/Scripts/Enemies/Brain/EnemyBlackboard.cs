using System.Collections.Generic;
using UnityEngine;

public sealed class EnemyBlackboard
{
    private EnemyInvestigationDebugData investigationDebugData;

    public EnemyTargetMemory TargetMemory { get; } = new();
    public EnemyPerceptionMemory PerceptionMemory { get; } = new();
    public EnemyInvestigationMemory InvestigationMemory { get; } = new();

    // Shared by Stalk, Retreat, Flank and Ambush, which are one behaviour in
    // four parts. Held here rather than in any one of them so the target,
    // the pose being planned against and the overall deadline survive a phase
    // change instead of being reset by it.
    public EnemyStealthManeuver StealthManeuver { get; } = new();

    public EnemyInvestigationDebugData InvestigationDebugData
    {
        get
        {
            if (!RuntimeDebugBuildGuard.IsEnabled)
                return null;

            return investigationDebugData ??= new EnemyInvestigationDebugData();
        }
    }

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
        StealthManeuver.End();
        investigationDebugData?.Clear();
    }
}
