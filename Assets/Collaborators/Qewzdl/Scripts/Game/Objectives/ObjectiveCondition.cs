using UnityEngine;

public abstract class ObjectiveCondition : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private string objectiveId = "objective";
    [SerializeField] private string displayName = "Objective";

    [Header("Completion")]
    [SerializeField] private bool completesGame = true;
    [SerializeField] private GameResultType resultType = GameResultType.Victory;
    [SerializeField] private string completionReason = "Objective completed";

    private ObjectiveManager manager;
    private GameplayEventHub eventHub;
    private bool isInitialized;
    private bool isRunning;
    private bool isCompleted;

    public string ObjectiveId => objectiveId;
    public string DisplayName => displayName;
    public bool CompletesGame => completesGame;
    public GameResultType ResultType => resultType;
    public string CompletionReason => completionReason;
    public bool IsCompleted => isCompleted;
    public bool IsRunning => isRunning;
    public virtual bool RequiresGameplayEventHub => false;
    public virtual int CurrentValue => isCompleted ? TargetValue : 0;
    public virtual int TargetValue => 1;

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

        if (string.IsNullOrWhiteSpace(objectiveId))
        {
            Debug.LogError($"{nameof(ObjectiveCondition)} has empty objective id.", this);
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