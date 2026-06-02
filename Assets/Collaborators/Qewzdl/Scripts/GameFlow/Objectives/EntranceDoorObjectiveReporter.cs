using UnityEngine;

[DisallowMultipleComponent]
public sealed class EntranceDoorObjectiveReporter : MonoBehaviour
{
    [SerializeField] private ObjectiveSceneBinding objectiveBinding;
    [SerializeField] private EntranceDoor entranceDoor;
    [SerializeField] private bool reportHandleProgress = true;
    [SerializeField] [Range(0f, 0.99f)] private float maxProgressBeforeUnlock = 0.99f;

    private void Reset()
    {
        ResolveReferences();
    }

    private void OnValidate()
    {
        ResolveReferences();
        maxProgressBeforeUnlock = Mathf.Clamp(maxProgressBeforeUnlock, 0f, 0.99f);
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (entranceDoor == null)
        {
            Debug.LogError($"{nameof(EntranceDoorObjectiveReporter)} requires assigned {nameof(EntranceDoor)}.", this);
            enabled = false;
            return;
        }

        if (objectiveBinding == null)
        {
            Debug.LogError($"{nameof(EntranceDoorObjectiveReporter)} requires assigned {nameof(ObjectiveSceneBinding)}.", this);
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
        if (!reportHandleProgress || !objectiveBinding.IsActive || totalHandleCount <= 0)
        {
            return;
        }

        float progress = Mathf.Clamp01(insertedHandleCount / (float)totalHandleCount);

        if (!entranceDoor.IsUnlocked)
        {
            progress = Mathf.Min(progress, maxProgressBeforeUnlock);
        }

        objectiveBinding.TryReportProgressServerOnly(progress, instigatorClientId);
    }

    private void HandleDoorUnlocked(ulong instigatorClientId)
    {
        objectiveBinding.TryCompleteServerOnly(instigatorClientId);
    }

    private void ResolveReferences()
    {
        if (objectiveBinding == null)
        {
            objectiveBinding = GetComponent<ObjectiveSceneBinding>()
                               ?? GetComponentInParent<ObjectiveSceneBinding>()
                               ?? GetComponentInChildren<ObjectiveSceneBinding>();
        }

        if (entranceDoor == null)
        {
            entranceDoor = GetComponent<EntranceDoor>()
                           ?? GetComponentInParent<EntranceDoor>()
                           ?? GetComponentInChildren<EntranceDoor>();
        }
    }
}
