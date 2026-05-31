using UnityEngine;

public sealed class CustomEventObjective : ObjectiveCondition
{
    [Header("Event")]
    [SerializeField] private string eventId = "objective.completed";
    [SerializeField] private int requiredEventCount = 1;

    private int currentEventCount;

    public override bool RequiresGameplayEventHub => true;
    public override int CurrentValue => currentEventCount;
    public override int TargetValue => Mathf.Max(1, requiredEventCount);

    public void RaiseObjectiveEvent()
    {
        RegisterEvent(0);
    }

    public void RegisterEvent(ulong instigatorClientId)
    {
        if (!CanRunServerLogic())
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

    protected override void OnObjectiveStarted()
    {
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
        if (eventData.EventId != eventId)
        {
            return;
        }

        RegisterEvent(eventData.ActorClientId);
    }
}