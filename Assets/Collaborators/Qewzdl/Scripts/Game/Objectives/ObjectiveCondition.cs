using UnityEngine;

public abstract class ObjectiveCondition : MonoBehaviour
{
    private ObjectiveManager manager;
    private GameplayEventHub eventHub;
    private bool isInitialized;
    private bool isRunning;
    private bool isCompleted;

    public abstract ObjectiveDefinition Definition { get; }

    public string ObjectiveId => Definition != null ? Definition.ObjectiveId : string.Empty;
    public string DisplayName => Definition != null ? Definition.DisplayName : string.Empty;
    public bool CompletesGame => Definition != null && Definition.CompletesGame;
    public GameResultType ResultType => Definition != null ? Definition.ResultType : GameResultType.None;
    public string CompletionReason => Definition != null ? Definition.CompletionReason : string.Empty;
    public bool IsCompleted => isCompleted;
    public bool IsRunning => isRunning;
    public virtual bool RequiresGameplayEventHub => false;
    public virtual int CurrentValue => isCompleted ? TargetValue : 0;
    public virtual int TargetValue => Definition != null ? Definition.TargetValue : 0;

    protected ObjectiveManager Manager => manager;
    protected GameplayEventHub EventHub => eventHub;

    internal void Initialize(ObjectiveManager objectiveManager, GameplayEventHub gameplayEventHub)
    {
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

        if (Definition.CompletesGame && Definition.ResultType == GameResultType.None)
        {
            Debug.LogError($"{Definition.name} completes game but has invalid result type.", Definition);
            enabled = false;
            return;
        }

        manager = objectiveManager;
        eventHub = gameplayEventHub;
        isInitialized = true;

        OnInitialized();
    }

    internal void StartObjectiveServerOnly()
    {
        if (!CanRunServerLogic())
        {
            return;
        }

        if (isRunning || isCompleted)
        {
            return;
        }

        isRunning = true;
        OnObjectiveStarted();
        NotifyProgressChanged();
    }

    internal void StopObjectiveServerOnly()
    {
        if (!isRunning)
        {
            return;
        }

        isRunning = false;
        OnObjectiveStopped();
        NotifyProgressChanged();
    }

    protected void Complete(ulong instigatorClientId = 0)
    {
        if (!CanRunServerLogic())
        {
            return;
        }

        if (!isRunning || isCompleted)
        {
            return;
        }

        isCompleted = true;
        isRunning = false;

        OnObjectiveCompleted();
        NotifyProgressChanged();

        manager.HandleObjectiveCompleted(this, instigatorClientId);
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
        return isInitialized && manager != null && manager.IsServerActive;
    }

    protected bool CanReceiveObjectiveSignal()
    {
        return CanRunServerLogic() && isRunning && !isCompleted;
    }

    protected virtual void OnInitialized()
    {
    }

    protected virtual void OnObjectiveStarted()
    {
    }

    protected virtual void OnObjectiveStopped()
    {
    }

    protected virtual void OnObjectiveCompleted()
    {
    }
}