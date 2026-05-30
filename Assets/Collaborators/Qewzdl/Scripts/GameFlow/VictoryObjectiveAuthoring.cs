using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class VictoryObjectiveAuthoring : MonoBehaviour
{
    [Header("Designer Setup")]
    [SerializeField] private string objectiveId;
    [SerializeField] private string displayName;
    [SerializeField] private bool isRequired = true;
    [SerializeField] private bool startsCompleted;

    [Header("Generated Runtime")]
    [SerializeField] private NetworkVictoryObjective runtimeObjective;

    public string ObjectiveId => objectiveId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;
    public bool IsRequired => isRequired;
    public bool StartsCompleted => startsCompleted;
    public NetworkVictoryObjective RuntimeObjective => runtimeObjective;

    private void Reset()
    {
        RefreshDefaultValues();
    }

    private void OnValidate()
    {
        RefreshDefaultValues();
    }

    private void RefreshDefaultValues()
    {
        if (string.IsNullOrWhiteSpace(objectiveId))
            objectiveId = CreateStableId(gameObject.name);

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = gameObject.name.Replace("_", " ");
    }

    public static string CreateStableId(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return "victory_objective";

        StringBuilder builder = new();

        for (int i = 0; i < rawName.Length; i++)
        {
            char character = char.ToLowerInvariant(rawName[i]);

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                continue;
            }

            if (character == '_' || character == '-' || character == ' ')
                builder.Append('_');
        }

        string result = builder.ToString().Trim('_');

        while (result.Contains("__"))
            result = result.Replace("__", "_");

        return string.IsNullOrWhiteSpace(result) ? "victory_objective" : result;
    }
}