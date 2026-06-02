using System;
using UnityEngine;

[CreateAssetMenu(
    fileName = "ObjectiveSequenceDefinition",
    menuName = "Wherever I Am/Game Flow/Objective Sequence")]
public sealed class ObjectiveSequenceDefinition : ScriptableObject
{
    [SerializeField] private ObjectiveDefinition[] objectives;

    public int Count => objectives == null ? 0 : objectives.Length;

    public ObjectiveDefinition GetObjective(int index)
    {
        if (objectives == null || index < 0 || index >= objectives.Length)
        {
            return null;
        }

        return objectives[index];
    }

    public bool IsValid(out string error)
    {
        if (objectives == null || objectives.Length == 0)
        {
            error = $"{nameof(ObjectiveSequenceDefinition)} '{name}' has no objectives.";
            return false;
        }

        for (int i = 0; i < objectives.Length; i++)
        {
            ObjectiveDefinition objective = objectives[i];

            if (objective == null)
            {
                error = $"{nameof(ObjectiveSequenceDefinition)} '{name}' has null objective at index {i}.";
                return false;
            }

            if (!objective.IsValid(out error))
            {
                error = $"{nameof(ObjectiveSequenceDefinition)} '{name}' has invalid objective at index {i}: {error}";
                return false;
            }

            for (int j = i + 1; j < objectives.Length; j++)
            {
                ObjectiveDefinition other = objectives[j];

                if (other == null)
                {
                    continue;
                }

                if (string.Equals(objective.ObjectiveId, other.ObjectiveId, StringComparison.Ordinal))
                {
                    error = $"{nameof(ObjectiveSequenceDefinition)} '{name}' has duplicate objective id '{objective.ObjectiveId}'.";
                    return false;
                }
            }
        }

        error = string.Empty;
        return true;
    }
}