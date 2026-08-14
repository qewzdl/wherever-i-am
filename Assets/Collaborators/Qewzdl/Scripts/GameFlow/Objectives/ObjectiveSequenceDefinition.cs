using System;
using UnityEngine;

[CreateAssetMenu(
    fileName = "ObjectiveSequenceDefinition",
    menuName = "Wherever I Am/Game Flow/Objective Sequence")]
public sealed class ObjectiveSequenceDefinition : ScriptableObject
{
    [SerializeField] private ObjectiveDefinition[] objectives;

    // What the match does about this list lives here rather than on each
    // objective, because it is the list that knows which one is last. An
    // objective asset then means the same thing wherever it is used, and the
    // same "open the door" can end one map and lead further into another.
    [Header("Result")]
    [Tooltip("What working through every objective does to the match.")]
    [SerializeField] private GameResultType completionResult = GameResultType.Victory;

    [SerializeField] private string completionReason = "All objectives completed";

    [Tooltip(
        "What losing one of these costs. None makes them all losable: the " +
        "sequence carries on to the next objective instead of ending.")]
    [SerializeField] private GameResultType failureResult = GameResultType.Defeat;

    [SerializeField] private string failureReason = "Objective failed";

    public int Count => objectives == null ? 0 : objectives.Length;
    public GameResultType CompletionResult => completionResult;
    public string CompletionReason => completionReason;
    public GameResultType FailureResult => failureResult;
    public string FailureReason => failureReason;
    public bool LosingAnObjectiveEndsMatch => failureResult != GameResultType.None;

    public bool IsLastObjective(int index)
    {
        return index >= 0 && index == Count - 1;
    }

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

        // Finishing the list has to mean something, or the match would run out
        // of objectives with nothing to show for it.
        if (completionResult == GameResultType.None)
        {
            error =
                $"{nameof(ObjectiveSequenceDefinition)} '{name}' has no completion result, " +
                "so working through it would decide nothing.";
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