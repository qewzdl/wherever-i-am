using UnityEngine;

[DisallowMultipleComponent]
public sealed class EntranceDoorObjectiveReporter : ObjectiveReporterBase
{
    [SerializeField] private EntranceDoor entranceDoor;
    [SerializeField] private bool reportHandleProgress = true;
    [SerializeField] [Range(0f, 0.99f)] private float maxProgressBeforeUnlock = 0.99f;

    protected override void Reset()
    {
        base.Reset();
        ResolveEntranceDoor();
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        ResolveEntranceDoor();
        maxProgressBeforeUnlock = Mathf.Clamp(maxProgressBeforeUnlock, 0f, 0.99f);
    }

    private void OnEnable()
    {
        ResolveEntranceDoor();

        if (entranceDoor == null)
        {
            Debug.LogError($"{nameof(EntranceDoorObjectiveReporter)} requires assigned {nameof(EntranceDoor)}.", this);
            enabled = false;
            return;
        }

        if (!ValidateReporterSetup())
        {
            enabled = false;
            return;
        }

        entranceDoor.HandleInserted += HandleDoorHandleInserted;
        entranceDoor.Unlocked += HandleDoorUnlocked;
    }

    private void OnDisable()
    {
        if (entranceDoor == null)
        {
            return;
        }

        entranceDoor.HandleInserted -= HandleDoorHandleInserted;
        entranceDoor.Unlocked -= HandleDoorUnlocked;
    }

    private void HandleDoorHandleInserted(int handleId, int insertedHandleCount, int totalHandleCount, ulong instigatorClientId)
    {
        if (!reportHandleProgress || !IsObjectiveActive || totalHandleCount <= 0)
        {
            return;
        }

        float progress = Mathf.Clamp01(insertedHandleCount / (float)totalHandleCount);

        if (!entranceDoor.IsUnlocked)
        {
            progress = Mathf.Min(progress, maxProgressBeforeUnlock);
        }

        ReportProgress(progress, instigatorClientId);
    }

    private void HandleDoorUnlocked(ulong instigatorClientId)
    {
        Complete(instigatorClientId);
    }

    private void ResolveEntranceDoor()
    {
        if (entranceDoor == null)
        {
            entranceDoor = GetComponent<EntranceDoor>()
                           ?? GetComponentInParent<EntranceDoor>()
                           ?? GetComponentInChildren<EntranceDoor>();
        }
    }
}
