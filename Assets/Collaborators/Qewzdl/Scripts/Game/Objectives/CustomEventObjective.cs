using System;
using UnityEngine;

public sealed class CustomEventObjective : ObjectiveCondition
{
    [Header("Definition")]
    [SerializeField] private CustomEventObjectiveDefinition definition;

    private int currentEventCount;

    public override ObjectiveDefinition Definition => definition;
    public override bool RequiresGameplayEventHub => true;
    public override int CurrentValue => currentEventCount;

    protected override void OnObjectiveStarted()
    {
        currentEventCount = 0;

        if (EventHub != null)
        {
            EventHub.GameplayEventRaised += HandleGameplayEventRaised;
        }
    }

    protected override void OnObjectiveStopped()
    {
        if (EventHub != null)
        {
            EventHub.GameplayEventRaised -= HandleGameplayEventRaised;
        }
    }

    protected override void OnObjectiveCompleted()
    {
        if (EventHub != null)
        {
            EventHub.GameplayEventRaised -= HandleGameplayEventRaised;
        }
    }

    private void HandleGameplayEventRaised(GameplayEventData eventData)
    {
        if (!CanReceiveObjectiveSignal())
        {
            return;
        }

        if (!string.Equals(eventData.EventId, definition.EventId, StringComparison.Ordinal))
        {
            return;
        }

        RegisterGameplayEvent(eventData.ActorClientId);
    }

    private void RegisterGameplayEvent(ulong instigatorClientId)
    {
        if (!CanReceiveObjectiveSignal())
        {
            return;
        }

        currentEventCount = Mathf.Min(currentEventCount + 1, TargetValue);
        NotifyProgressChanged();

        if (currentEventCount >= TargetValue)
        {
            Complete(instigatorClientId);
        }
    }
}