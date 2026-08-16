using System.Text;
using UnityEngine;
using UnityEngine.Serialization;

// The asset is the identity. It carried a hand-typed id as well until it became
// clear that everything holding an objective already held a reference to it,
// and the string only added a second name that could disagree with the first.
[CreateAssetMenu(
    fileName = "ObjectiveDefinition",
    menuName = "Wherever I Am/Game Flow/Objective Definition")]
public class ObjectiveDefinition : ScriptableObject
{
    // GameResultData carries the source in a FixedString64Bytes.
    private const int MaxSourceIdUtf8Bytes = 60;

    [Header("Presentation")]
    [FormerlySerializedAs("displayName")]
    [SerializeField] private string title;
    [SerializeField] [TextArea] private string description;

    [Header("Runtime")]
    [SerializeField] private bool requiresSceneBinding = true;
    [FormerlySerializedAs("targetValue")]
    [SerializeField] [Min(0.0001f)] private float requiredProgress = 1f;

    public string Title => title;
    public string Description => description;
    public string DisplayName => string.IsNullOrWhiteSpace(title) ? name : title;
    public bool RequiresSceneBinding => requiresSceneBinding;
    public float RequiredProgress => Mathf.Max(0.0001f, requiredProgress);
    public int TargetValue => Mathf.Max(1, Mathf.CeilToInt(requiredProgress));

    // Not identity - only what a finished match reports as having decided it.
    public string SourceId => name;

    public bool IsValid(out string error)
    {
        if (Encoding.UTF8.GetByteCount(name) > MaxSourceIdUtf8Bytes)
        {
            error =
                $"{nameof(ObjectiveDefinition)} name '{name}' is too long to report as a " +
                $"match result source. Max UTF8 bytes: {MaxSourceIdUtf8Bytes}.";
            return false;
        }

        if (requiredProgress <= 0f)
        {
            error = $"{nameof(ObjectiveDefinition)} '{name}' requires progress greater than zero.";
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
