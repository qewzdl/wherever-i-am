using UnityEngine;

public abstract class ObjectiveCondition : MonoBehaviour
{
    private ObjectiveManager manager;
    private GameplayEventHub eventHub;
    private ObjectiveState state = ObjectiveState.Inactive;

    public abstract ObjectiveDefinition Definition { get; }

    public string ObjectiveId => Definition != null ? Definition.ObjectiveId : string.Empty;
    public string DisplayName => Definition != null ? Definition.DisplayName : string.Empty;
    public ObjectiveState State => state;
    public bool IsCompleted => state == ObjectiveState.Completed;
    public bool IsRunning => state == ObjectiveState.Running;
    public bool IsTerminal => state == ObjectiveState.Completed
                              || state == ObjectiveState.Failed
                              || state == ObjectiveState.Cancelled;
    public virtual bool RequiresGameplayEventHub => false;
    public virtual int CurrentValue => IsCompleted ? TargetValue : 0;
    public virtual int TargetValue => Definition != null ? Definition.TargetValue : 0;

    protected ObjectiveManager Manager => manager;
    protected GameplayEventHub EventHub => eventHub;

    internal void Initialize(ObjectiveManager objectiveManager, GameplayEventHub gameplayEventHub)
    {
        if (state != ObjectiveState.Inactive)
        {
            Debug.LogError($"{GetType().Name} can be initialized only from {nameof(ObjectiveState.Inactive)}. Current state: {state}.", this);
            enabled = false;
            return;
        }

        if (objectiveManager == null)
        {
            Debug.LogError($"{nameof(ObjectiveCondition)} requires {nameof(ObjectiveManager)}.", this);
            enabled = false;
            return;
        }

        if (Definition == null)
        {
            Debug.LogError($"{GetType().Name} requires assigned {nameof(ObjectiveDefinition)}.", this);
            enabled = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(Definition.ObjectiveId))
        {
            Debug.LogError($"{Definition.name} has empty objective id.", Definition);
            enabled = false;
            return;
        }

        manager = objectiveManager;
        eventHub = gameplayEventHub;
        SetState(ObjectiveState.Initialized);

        OnInitialized();
    }

    internal bool CanStartObjectiveServerOnly()
    {
        return CanRunServerLogic() && state == ObjectiveState.Initialized;
    }

    internal bool StartObjectiveServerOnly()
    {
        if (!CanRunServerLogic())
        {
            Debug.LogError($"{GetType().Name} cannot start objective because server logic is not available. Current state: {state}.", this);
            return false;
        }

        if (state != ObjectiveState.Initialized)
        {
            Debug.LogError($"{GetType().Name} can start only from {nameof(ObjectiveState.Initialized)}. Current state: {state}.", this);
            return false;
        }

        SetState(ObjectiveState.Running);
        OnObjectiveStarted();
        NotifyProgressChanged();

        if (state != ObjectiveState.Running)
        {
            Debug.LogError($"{GetType().Name} failed to stay in {nameof(ObjectiveState.Running)} after start. Current state: {state}.", this);
            return false;
        }

        return true;
    }

    internal void CancelObjectiveServerOnly(ulong instigatorClientId = 0)
    {
        if (!CanRunServerLogic())
        {
            return;
        }

        if (state != ObjectiveState.Initialized && state != ObjectiveState.Running)
        {
            return;
        }

        SetState(ObjectiveState.Cancelled);
        OnObjectiveCancelled();
        NotifyProgressChanged();

        manager.HandleObjectiveCancelled(this, instigatorClientId);
    }

    protected void Complete(ulong instigatorClientId = 0)
    {
        if (!CanRunServerLogic())
        {
            return;
        }

        if (state != ObjectiveState.Running)
        {
            return;
        }

        SetState(ObjectiveState.Completed);
        OnObjectiveCompleted();
        NotifyProgressChanged();

        manager.HandleObjectiveCompleted(this, instigatorClientId);
    }

    protected void Fail(ulong instigatorClientId = 0)
    {
        if (!CanRunServerLogic())
        {
            return;
        }

        if (state != ObjectiveState.Running)
        {
            return;
        }

        SetState(ObjectiveState.Failed);
        OnObjectiveFailed();
        NotifyProgressChanged();

        manager.HandleObjectiveFailed(this, instigatorClientId);
    }

    protected void NotifyProgressChanged()
    {
        if (!CanRunServerLogic())
        {
            return;
        }

        manager.UpdateObjectiveProgress(this);
    }

    protected bool CanRunServerLogic()
    {
        return state != ObjectiveState.Inactive && manager != null && manager.IsServerActive;
    }

    protected bool CanReceiveObjectiveSignal()
    {
        return CanRunServerLogic() && state == ObjectiveState.Running;
    }

    private void SetState(ObjectiveState nextState)
    {
        state = nextState;
    }

    protected virtual void OnInitialized()
    {
    }

    protected virtual void OnObjectiveStarted()
    {
    }

    protected virtual void OnObjectiveCompleted()
    {
    }

    protected virtual void OnObjectiveFailed()
    {
    }

    protected virtual void OnObjectiveCancelled()
    {
    }
}