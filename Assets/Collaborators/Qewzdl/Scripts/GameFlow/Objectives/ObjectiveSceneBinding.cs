using UnityEngine;

public sealed class ObjectiveSceneBinding : MonoBehaviour
{
    [SerializeField] private ObjectiveDefinition objective;

    private NetworkObjectiveFlow objectiveFlow;
    private bool isActive;

    public ObjectiveDefinition Objective => objective;
    public bool IsActive => isActive;
    public bool IsBound => objectiveFlow != null;
    public bool IsServerBound => objectiveFlow != null && objectiveFlow.IsServer;
    public bool CanReportServerOnly => IsServerBound && isActive && objective != null;

    public bool IsConfigured(out string error)
    {
        if (objective == null)
        {
            error = $"{nameof(ObjectiveSceneBinding)} on '{name}' has no objective definition.";
            return false;
        }

        if (!objective.IsValid(out error))
        {
            error = $"{nameof(ObjectiveSceneBinding)} on '{name}' has invalid objective definition: {error}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public void Bind(NetworkObjectiveFlow flow)
    {
        if (flow == null)
        {
            Debug.LogError($"{nameof(ObjectiveSceneBinding)} '{name}' received null objective flow.", this);
            enabled = false;
            return;
        }

        objectiveFlow = flow;
        SetActiveState(false);
    }

    public void Unbind()
    {
        SetActiveState(false);
        objectiveFlow = null;
    }

    public void SetActiveState(bool active)
    {
        isActive = active;
    }

    public bool TryReportProgressServerOnly(float progress01, ulong instigatorClientId = 0)
    {
        if (!ValidateCanReportServerOnly())
        {
            return false;
        }

        return objectiveFlow.ReportObjectiveProgressNormalizedServerOnly(objective, progress01, instigatorClientId);
    }

    public bool TryReportProgressAmountServerOnly(float progressAmount, ulong instigatorClientId = 0)
    {
        if (!ValidateCanReportServerOnly())
        {
            return false;
        }

        return objectiveFlow.ReportObjectiveProgressAmountServerOnly(objective, progressAmount, instigatorClientId);
    }

    public bool TryCompleteServerOnly(ulong instigatorClientId = 0)
    {
        if (!ValidateCanReportServerOnly())
        {
            return false;
        }

        return objectiveFlow.CompleteObjectiveServerOnly(objective, instigatorClientId);
    }

    public bool TryFailServerOnly(ulong instigatorClientId = 0)
    {
        if (!ValidateCanReportServerOnly())
        {
            return false;
        }

        return objectiveFlow.FailObjectiveServerOnly(objective, instigatorClientId);
    }

    private bool ValidateCanReportServerOnly()
    {
        if (objectiveFlow == null)
        {
            Debug.LogError($"{nameof(ObjectiveSceneBinding)} '{name}' is not bound to {nameof(NetworkObjectiveFlow)}.", this);
            return false;
        }

        if (!objectiveFlow.IsServer)
        {
            Debug.LogError($"{nameof(ObjectiveSceneBinding)} '{name}' can report objective progress only on server.", this);
            return false;
        }

        if (!isActive)
        {
            return false;
        }

        if (objective == null)
        {
            Debug.LogError($"{nameof(ObjectiveSceneBinding)} '{name}' has no objective.", this);
            return false;
        }

        return true;
    }
}
