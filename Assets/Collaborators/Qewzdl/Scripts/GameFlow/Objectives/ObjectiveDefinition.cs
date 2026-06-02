using System.Text;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(
    fileName = "ObjectiveDefinition",
    menuName = "Wherever I Am/Game Flow/Objective Definition")]
public class ObjectiveDefinition : ScriptableObject
{
    private const int MaxObjectiveIdUtf8Bytes = 60;

    [Header("Identity")]
    [SerializeField] private string objectiveId;

    [Header("Presentation")]
    [FormerlySerializedAs("displayName")]
    [SerializeField] private string title;
    [SerializeField] [TextArea] private string description;

    [Header("Runtime")]
    [SerializeField] private bool requiresSceneBinding = true;
    [FormerlySerializedAs("targetValue")]
    [SerializeField] [Min(0.0001f)] private float requiredProgress = 1f;

    [Header("Completion")]
    [SerializeField] private ObjectiveCompletionPolicy completionPolicy = ObjectiveCompletionPolicy.CompletesGame;
    [FormerlySerializedAs("resultType")]
    [SerializeField] private GameResultType completionResult = GameResultType.Victory;
    [SerializeField] private string completionReason = "Objective completed";

    public string ObjectiveId => objectiveId;
    public string Title => title;
    public string Description => description;
    public string DisplayName => string.IsNullOrWhiteSpace(title) ? objectiveId : title;
    public bool RequiresSceneBinding => requiresSceneBinding;
    public float RequiredProgress => Mathf.Max(0.0001f, requiredProgress);
    public int TargetValue => Mathf.Max(1, Mathf.CeilToInt(requiredProgress));
    public ObjectiveCompletionPolicy CompletionPolicy => completionPolicy;
    public bool CompletesGame => completionPolicy == ObjectiveCompletionPolicy.CompletesGame;
    public GameResultType ResultType => CompletesGame ? completionResult : GameResultType.None;
    public GameResultType CompletionResult => completionResult;
    public string CompletionReason => completionReason;

    public bool IsValid(out string error)
    {
        if (string.IsNullOrWhiteSpace(objectiveId))
        {
            error = $"{nameof(ObjectiveDefinition)} has empty objective id.";
            return false;
        }

        if (Encoding.UTF8.GetByteCount(objectiveId) > MaxObjectiveIdUtf8Bytes)
        {
            error = $"{nameof(ObjectiveDefinition)} id '{objectiveId}' is too long. Max UTF8 bytes: {MaxObjectiveIdUtf8Bytes}.";
            return false;
        }

        if (requiredProgress <= 0f)
        {
            error = $"{nameof(ObjectiveDefinition)} '{objectiveId}' requires progress greater than zero.";
            return false;
        }

        if (completionResult == GameResultType.None)
        {
            error = $"{nameof(ObjectiveDefinition)} '{objectiveId}' has invalid completion result.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    protected virtual void OnValidate()
    {
        requiredProgress = Mathf.Max(0.0001f, requiredProgress);
    }
}
