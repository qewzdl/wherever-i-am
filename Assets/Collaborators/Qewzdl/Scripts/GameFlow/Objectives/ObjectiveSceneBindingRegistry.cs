using System;
using UnityEngine;

public sealed class ObjectiveSceneBindingRegistry : MonoBehaviour
{
    [Tooltip(
        "Leave empty to collect every binding under this object. Fill it in " +
        "only to bind a hand-picked set, or bindings that live outside this " +
        "hierarchy.")]
    [SerializeField] private ObjectiveSceneBinding[] bindings = Array.Empty<ObjectiveSceneBinding>();

    // The serialized field is the designer's explicit override; this is what is
    // actually used. Writing the collected children back into it would quietly
    // turn "collect automatically" into "this exact list", and every binding
    // added to the map afterwards would be ignored - which is the mistake the
    // hand-kept array made easy in the first place.
    private ObjectiveSceneBinding[] resolvedBindings;

    private ObjectiveSceneBinding[] Bindings
    {
        get
        {
            if (bindings != null && bindings.Length > 0)
            {
                return bindings;
            }

            if (resolvedBindings == null)
            {
                resolvedBindings = GetComponentsInChildren<ObjectiveSceneBinding>(true);
            }

            return resolvedBindings;
        }
    }

    public bool IsValidForSequence(ObjectiveSequenceDefinition sequence, out string error)
    {
        ObjectiveSceneBinding[] resolved = Bindings;

        for (int i = 0; i < resolved.Length; i++)
        {
            ObjectiveSceneBinding binding = resolved[i];

            if (binding == null)
            {
                error = $"{nameof(ObjectiveSceneBindingRegistry)} on '{name}' has null binding at index {i}.";
                return false;
            }

            if (!binding.IsConfigured(out error))
            {
                return false;
            }

            for (int j = i + 1; j < resolved.Length; j++)
            {
                ObjectiveSceneBinding other = resolved[j];

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

        ObjectiveSceneBinding[] resolved = Bindings;

        if (resolved == null)
        {
            error =
                $"{nameof(ObjectiveSceneBindingRegistry)} on '{name}' has no bindings.";
            return false;
        }

        for (int i = 0; i < resolved.Length; i++)
        {
            ObjectiveSceneBinding binding = resolved[i];

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
        ObjectiveSceneBinding[] resolved = Bindings;

        if (resolved == null)
        {
            return;
        }

        for (int i = 0; i < resolved.Length; i++)
        {
            ObjectiveSceneBinding binding = resolved[i];

            if (binding == null)
            {
                continue;
            }

            binding.SetActiveState(false);
        }
    }

    public void UnbindAll()
    {
        ObjectiveSceneBinding[] resolved = Bindings;

        if (resolved == null)
        {
            return;
        }

        for (int i = 0; i < resolved.Length; i++)
        {
            ObjectiveSceneBinding binding = resolved[i];

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

        ObjectiveSceneBinding[] resolved = Bindings;

        if (resolved == null || objective == null)
        {
            return false;
        }

        for (int i = 0; i < resolved.Length; i++)
        {
            ObjectiveSceneBinding candidate = resolved[i];

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
