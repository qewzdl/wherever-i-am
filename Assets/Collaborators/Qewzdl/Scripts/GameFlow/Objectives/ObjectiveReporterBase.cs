using UnityEngine;

public abstract class ObjectiveReporterBase : MonoBehaviour
{
    [SerializeField] private ObjectiveSceneBinding objectiveBinding;

    protected ObjectiveSceneBinding ObjectiveBinding => objectiveBinding;
    protected ObjectiveDefinition ObjectiveDefinition => objectiveBinding != null ? objectiveBinding.Objective : null;
    protected bool IsObjectiveActive => objectiveBinding != null && objectiveBinding.IsActive;
    protected bool CanReportObjectiveNow => objectiveBinding != null && objectiveBinding.CanReportServerOnly;

    protected virtual void Reset()
    {
        ResolveObjectiveBinding();
    }

    protected virtual void OnValidate()
    {
        ResolveObjectiveBinding();
    }

    protected bool ValidateReporterSetup()
    {
        ResolveObjectiveBinding();

        if (objectiveBinding != null)
        {
            return true;
        }

        Debug.LogError($"{GetType().Name} requires assigned {nameof(ObjectiveSceneBinding)}.", this);
        return false;
    }

    protected bool ReportProgress(float progress01, ulong instigatorClientId = 0)
    {
        if (!CanReportObjective("report objective progress"))
        {
            return false;
        }

        return objectiveBinding.TryReportProgressServerOnly(progress01, instigatorClientId);
    }

    protected bool ReportAmount(float progressAmount, ulong instigatorClientId = 0)
    {
        if (!CanReportObjective("report objective progress amount"))
        {
            return false;
        }

        return objectiveBinding.TryReportProgressAmountServerOnly(progressAmount, instigatorClientId);
    }

    protected bool Complete(ulong instigatorClientId = 0)
    {
        if (!CanReportObjective("complete objective"))
        {
            return false;
        }

        return objectiveBinding.TryCompleteServerOnly(instigatorClientId);
    }

    protected bool Fail(ulong instigatorClientId = 0)
    {
        if (!CanReportObjective("fail objective"))
        {
            return false;
        }

        return objectiveBinding.TryFailServerOnly(instigatorClientId);
    }

    protected void ResolveObjectiveBinding()
    {
        if (objectiveBinding != null)
        {
            return;
        }

        objectiveBinding = GetComponent<ObjectiveSceneBinding>()
                           ?? GetComponentInParent<ObjectiveSceneBinding>()
                           ?? GetComponentInChildren<ObjectiveSceneBinding>();
    }

    private bool CanReportObjective(string action)
    {
        if (!ValidateReporterSetup())
        {
            return false;
        }

        if (!objectiveBinding.IsBound)
        {
            Debug.LogError($"{GetType().Name} cannot {action}: {nameof(ObjectiveSceneBinding)} is not bound to {nameof(NetworkObjectiveFlow)}.", this);
            return false;
        }

        if (!objectiveBinding.IsServerBound)
        {
            Debug.LogError($"{GetType().Name} cannot {action}: objective reporters can report only on server.", this);
            return false;
        }

        if (!objectiveBinding.IsActive)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(objectiveBinding.ObjectiveId))
        {
            Debug.LogError($"{GetType().Name} cannot {action}: objective id is empty.", this);
            return false;
        }

        return true;
    }
}
