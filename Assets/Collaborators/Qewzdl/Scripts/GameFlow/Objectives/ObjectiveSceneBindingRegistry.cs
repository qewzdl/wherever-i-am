using System;
using UnityEngine;

public sealed class ObjectiveSceneBindingRegistry : MonoBehaviour
{
    [SerializeField] private ObjectiveSceneBinding[] bindings;

    public bool IsValidForSequence(ObjectiveSequenceDefinition sequence, out string error)
    {
        if (bindings == null)
        {
            error = $"{nameof(ObjectiveSceneBindingRegistry)} on '{name}' has null bindings array.";
            return false;
        }

        for (int i = 0; i < bindings.Length; i++)
        {
            ObjectiveSceneBinding binding = bindings[i];

            if (binding == null)
            {
                error = $"{nameof(ObjectiveSceneBindingRegistry)} on '{name}' has null binding at index {i}.";
                return false;
            }

            if (!binding.IsConfigured(out error))
            {
                return false;
            }

            for (int j = i + 1; j < bindings.Length; j++)
            {
                ObjectiveSceneBinding other = bindings[j];

                if (other == null || other.Objective == null)
                {
                    continue;
                }

                if (string.Equals(binding.ObjectiveId, other.ObjectiveId, StringComparison.Ordinal))
                {
                    error = $"{nameof(ObjectiveSceneBindingRegistry)} on '{name}' has duplicate scene binding for objective '{binding.ObjectiveId}'.";
                    return false;
                }
            }
        }

        if (sequence == null)
        {
            error = $"{nameof(ObjectiveSceneBindingRegistry)} on '{name}' received null objective sequence.";
            return false;
        }

        for (int i = 0; i < sequence.Count; i++)
        {
            ObjectiveDefinition objective = sequence.GetObjective(i);

            if (objective == null || !objective.RequiresSceneBinding)
            {
                continue;
            }

            if (!TryGetBinding(objective.ObjectiveId, out _))
            {
                error = $"{nameof(ObjectiveSceneBindingRegistry)} on '{name}' has no scene binding for required objective '{objective.ObjectiveId}'.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public void BindAll(NetworkObjectiveFlow flow)
    {
        if (bindings == null)
        {
            Debug.LogError($"{nameof(ObjectiveSceneBindingRegistry)} on '{name}' has null bindings array.", this);
            enabled = false;
            return;
        }

        for (int i = 0; i < bindings.Length; i++)
        {
            ObjectiveSceneBinding binding = bindings[i];

            if (binding == null)
            {
                Debug.LogError($"{nameof(ObjectiveSceneBindingRegistry)} on '{name}' has null binding at index {i}.", this);
                enabled = false;
                return;
            }

            binding.Bind(flow);
        }
    }

    public void DeactivateAll()
    {
        if (bindings == null)
        {
            return;
        }

        for (int i = 0; i < bindings.Length; i++)
        {
            ObjectiveSceneBinding binding = bindings[i];

            if (binding == null)
            {
                continue;
            }

            binding.SetActiveState(false);
        }
    }

    public void UnbindAll()
    {
        if (bindings == null)
        {
            return;
        }

        for (int i = 0; i < bindings.Length; i++)
        {
            ObjectiveSceneBinding binding = bindings[i];

            if (binding == null)
            {
                continue;
            }

            binding.Unbind();
        }
    }

    public bool TryGetBinding(string objectiveId, out ObjectiveSceneBinding binding)
    {
        binding = null;

        if (bindings == null || string.IsNullOrWhiteSpace(objectiveId))
        {
            return false;
        }

        for (int i = 0; i < bindings.Length; i++)
        {
            ObjectiveSceneBinding candidate = bindings[i];

            if (candidate == null)
            {
                continue;
            }

            if (string.Equals(candidate.ObjectiveId, objectiveId, StringComparison.Ordinal))
            {
                binding = candidate;
                return true;
            }
        }

        return false;
    }
}
