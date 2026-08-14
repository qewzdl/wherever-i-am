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

                if (other.Objective == binding.Objective)
                {
                    error =
                        $"{nameof(ObjectiveSceneBindingRegistry)} on '{name}' has two scene " +
                        $"bindings for objective '{binding.Objective.name}'.";
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

            if (!TryGetBinding(objective, out _))
            {
                error =
                    $"{nameof(ObjectiveSceneBindingRegistry)} on '{name}' has no scene " +
                    $"binding for required objective '{objective.name}'.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public bool TryBindAll(NetworkObjectiveFlow flow, out string error)
    {
        if (flow == null)
        {
            error =
                $"{nameof(ObjectiveSceneBindingRegistry)} on '{name}' received null objective flow.";
            return false;
        }

        if (bindings == null)
        {
            error =
                $"{nameof(ObjectiveSceneBindingRegistry)} on '{name}' has null bindings array.";
            return false;
        }

        for (int i = 0; i < bindings.Length; i++)
        {
            ObjectiveSceneBinding binding = bindings[i];

            if (binding == null)
            {
                UnbindAll();
                error =
                    $"{nameof(ObjectiveSceneBindingRegistry)} on '{name}' has null binding at index {i}.";
                return false;
            }

            try
            {
                binding.Bind(flow);
            }
            catch (Exception exception)
            {
                UnbindAll();
                error =
                    $"{nameof(ObjectiveSceneBindingRegistry)} on '{name}' " +
                    $"failed to bind objective at index {i}: {exception.Message}";
                return false;
            }
        }

        error = string.Empty;
        return true;
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

    public bool TryGetBinding(ObjectiveDefinition objective, out ObjectiveSceneBinding binding)
    {
        binding = null;

        if (bindings == null || objective == null)
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

            if (candidate.Objective == objective)
            {
                binding = candidate;
                return true;
            }
        }

        return false;
    }

#if UNITY_EDITOR
    public void ConfigureEditor(ObjectiveSceneBinding[] sceneBindings)
    {
        bindings = sceneBindings ?? Array.Empty<ObjectiveSceneBinding>();
    }
#endif
}
