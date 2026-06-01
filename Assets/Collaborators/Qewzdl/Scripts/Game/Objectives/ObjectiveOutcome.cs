public struct ObjectiveOutcome
{
    public ObjectiveCondition Objective;
    public ObjectiveState State;
    public ulong InstigatorClientId;

    public bool IsCompleted => State == ObjectiveState.Completed;
    public bool IsFailed => State == ObjectiveState.Failed;
    public bool IsTerminal => IsCompleted || IsFailed;

    public static ObjectiveOutcome Completed(ObjectiveCondition objective, ulong instigatorClientId)
    {
        return new ObjectiveOutcome
        {
            Objective = objective,
            State = ObjectiveState.Completed,
            InstigatorClientId = instigatorClientId
        };
    }

    public static ObjectiveOutcome Failed(ObjectiveCondition objective, ulong instigatorClientId)
    {
        return new ObjectiveOutcome
        {
            Objective = objective,
            State = ObjectiveState.Failed,
            InstigatorClientId = instigatorClientId
        };
    }
}