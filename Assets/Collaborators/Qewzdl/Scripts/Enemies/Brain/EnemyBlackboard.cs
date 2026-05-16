using System.Collections.Generic;
using UnityEngine;

public sealed class EnemyBlackboard
{
    private readonly List<EnemyInvestigationSearchPoint> currentInvestigationRoute = new();

    public EnemyTargetMemory TargetMemory { get; } = new();
    public EnemyInvestigationDebugData InvestigationDebugData { get; } = new();

    public EnemyTarget CurrentTarget => TargetMemory.CurrentTarget;

    public EnemyPerceptionStimulus CurrentStimulus { get; private set; } = EnemyPerceptionStimulus.None;

    public EnemyPosture CurrentPosture { get; private set; } = EnemyPosture.Standing;

    public Vector3 CurrentDestination { get; private set; }
    public bool HasCurrentDestination { get; private set; }

    public Vector3 SuspiciousPosition => TargetMemory.SecondarySuspiciousPosition;
    public bool HasSuspiciousPosition => TargetMemory.HasSecondarySuspiciousPosition;

    public Vector3 LastKnownTargetPosition => TargetMemory.LastKnownTargetPosition;
    public bool HasLastKnownTargetPosition => TargetMemory.HasLastKnownTargetPosition;

    public float LastVisibleTime { get; private set; } = -1f;
    public float LastHeardTime { get; private set; } = -1f;

    public IReadOnlyList<EnemyInvestigationSearchPoint> CurrentInvestigationRoute => currentInvestigationRoute;

    public void SetCurrentStimulus(EnemyPerceptionStimulus stimulus, float serverTime)
    {
        CurrentStimulus = stimulus;

        if (!stimulus.HasStimulus)
        {
            return;
        }

        if (stimulus.Source == EnemyPerceptionSource.Vision)
        {
            LastVisibleTime = serverTime;
            return;
        }

        if (stimulus.Source == EnemyPerceptionSource.Hearing)
        {
            LastHeardTime = serverTime;
        }
    }

    public void ClearCurrentStimulus()
    {
        CurrentStimulus = EnemyPerceptionStimulus.None;
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
        currentInvestigationRoute.Clear();

        if (route == null)
        {
            return;
        }

        for (int i = 0; i < route.Count; i++)
        {
            currentInvestigationRoute.Add(route[i]);
        }
    }

    public void ClearCurrentInvestigationRoute()
    {
        currentInvestigationRoute.Clear();
    }

    public void ClearTargetMemory()
    {
        TargetMemory.ClearAll();
    }

    public void ClearAll()
    {
        ClearCurrentStimulus();
        ClearCurrentDestination();
        ClearCurrentInvestigationRoute();

        LastVisibleTime = -1f;
        LastHeardTime = -1f;
        CurrentPosture = EnemyPosture.Standing;

        TargetMemory.ClearAll();
        InvestigationDebugData.Clear();
    }
}