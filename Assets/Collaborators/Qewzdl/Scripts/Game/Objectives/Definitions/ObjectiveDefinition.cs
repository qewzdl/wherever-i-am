using UnityEngine;

public abstract class ObjectiveDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string objectiveId = "objective";
    [SerializeField] private string displayName = "Objective";

    [Header("Progress")]
    [SerializeField] [Min(1)] private int targetValue = 1;

    [Header("Completion")]
    [SerializeField] private ObjectiveCompletionPolicy completionPolicy = ObjectiveCompletionPolicy.CompletesGame;
    [SerializeField] private GameResultType resultType = GameResultType.Victory;
    [SerializeField] private string completionReason = "Objective completed";

    public string ObjectiveId => objectiveId;
    public string DisplayName => displayName;
    public int TargetValue => Mathf.Max(1, targetValue);
    public ObjectiveCompletionPolicy CompletionPolicy => completionPolicy;
    public bool CompletesGame => completionPolicy == ObjectiveCompletionPolicy.CompletesGame;
    public GameResultType ResultType => CompletesGame ? resultType : GameResultType.None;
    public string CompletionReason => completionReason;

    protected virtual void OnValidate()
    {
        targetValue = Mathf.Max(1, targetValue);
    }
}