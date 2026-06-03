using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class TriggerZoneObjectiveReporter : ObjectiveReporterBase
{
    [SerializeField] private string requiredTag = "Player";
    [SerializeField] private bool countUniqueClients = true;
    [SerializeField] private bool completeWhenTargetReached = true;

    private readonly HashSet<ulong> enteredClientIds = new HashSet<ulong>();
    private int currentEntries;
    private Collider triggerCollider;

    protected override void Reset()
    {
        base.Reset();
        ResolveCollider();
        EnsureTriggerCollider();
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        ResolveCollider();
        EnsureTriggerCollider();
    }

    private void OnEnable()
    {
        ResolveCollider();

        if (triggerCollider == null)
        {
            Debug.LogError($"{nameof(TriggerZoneObjectiveReporter)} requires assigned {nameof(Collider)}.", this);
            enabled = false;
            return;
        }

        if (!ValidateReporterSetup())
        {
            enabled = false;
        }
    }

    private void OnDisable()
    {
        enteredClientIds.Clear();
        currentEntries = 0;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!CanReportObjectiveNow || !PassesTagFilter(other))
        {
            return;
        }

        NetworkObject networkObject = other.GetComponentInParent<NetworkObject>();

        if (networkObject == null || !networkObject.IsSpawned)
        {
            return;
        }

        ulong actorClientId = networkObject.OwnerClientId;

        if (countUniqueClients)
        {
            if (!enteredClientIds.Add(actorClientId))
            {
                return;
            }

            currentEntries = enteredClientIds.Count;
        }
        else
        {
            currentEntries++;
        }

        int targetValue = GetTargetValue();
        currentEntries = Mathf.Min(currentEntries, targetValue);

        ReportAmount(currentEntries, actorClientId);

        if (completeWhenTargetReached && currentEntries >= targetValue)
        {
            Complete(actorClientId);
        }
    }

    private bool PassesTagFilter(Collider other)
    {
        return string.IsNullOrWhiteSpace(requiredTag) || other.CompareTag(requiredTag);
    }

    private int GetTargetValue()
    {
        return ObjectiveDefinition != null ? ObjectiveDefinition.TargetValue : 1;
    }

    private void ResolveCollider()
    {
        if (triggerCollider == null)
        {
            triggerCollider = GetComponent<Collider>();
        }
    }

    private void EnsureTriggerCollider()
    {
        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
        }
    }
}
