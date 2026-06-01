using System;
using Unity.Netcode;

public sealed class ObjectiveProgressSync
{
    private readonly NetworkList<ObjectiveProgressData> progressStates;

    public event Action<ObjectiveProgressData> ProgressChanged;

    public ObjectiveProgressSync(NetworkList<ObjectiveProgressData> networkProgressStates)
    {
        progressStates = networkProgressStates ?? throw new ArgumentNullException(nameof(networkProgressStates));
    }

    public int ProgressCount => progressStates.Count;

    public ObjectiveProgressData GetProgress(int index)
    {
        return progressStates[index];
    }

    public void Subscribe()
    {
        progressStates.OnListChanged += HandleProgressListChanged;
    }

    public void Unsubscribe()
    {
        progressStates.OnListChanged -= HandleProgressListChanged;
    }

    public void ClearServerOnly()
    {
        progressStates.Clear();
    }

    public void UpsertObjectiveServerOnly(ObjectiveCondition objective)
    {
        if (objective == null)
        {
            return;
        }

        ObjectiveProgressData progress = ObjectiveProgressData.Create(
            objective.ObjectiveId,
            objective.DisplayName,
            objective.CurrentValue,
            objective.TargetValue,
            objective.IsCompleted,
            objective.State);

        int index = FindProgressIndex(objective.ObjectiveId);

        if (index >= 0)
        {
            progressStates[index] = progress;
            return;
        }

        progressStates.Add(progress);
    }

    private int FindProgressIndex(string objectiveId)
    {
        for (int i = 0; i < progressStates.Count; i++)
        {
            if (progressStates[i].ObjectiveId.ToString() == objectiveId)
            {
                return i;
            }
        }

        return -1;
    }

    private void HandleProgressListChanged(NetworkListEvent<ObjectiveProgressData> changeEvent)
    {
        ProgressChanged?.Invoke(changeEvent.Value);
    }
}