using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public sealed class TriggerZoneObjective : ObjectiveCondition
{
    [Header("Definition")]
    [SerializeField] private TriggerZoneObjectiveDefinition definition;

    private readonly HashSet<ulong> enteredClientIds = new HashSet<ulong>();
    private int currentEntries;

    public override ObjectiveDefinition Definition => definition;
    public override int CurrentValue => currentEntries;

    protected override void OnObjectiveStarted()
    {
        enteredClientIds.Clear();
        currentEntries = 0;
    }

    protected override void OnObjectiveStopped()
    {
        enteredClientIds.Clear();
    }

    protected override void OnObjectiveCompleted()
    {
        enteredClientIds.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!CanReceiveObjectiveSignal())
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(definition.RequiredTag) && !other.CompareTag(definition.RequiredTag))
        {
            return;
        }

        NetworkObject networkObject = other.GetComponentInParent<NetworkObject>();

        if (networkObject == null || !networkObject.IsSpawned)
        {
            return;
        }

        ulong actorClientId = networkObject.OwnerClientId;

        if (definition.CountUniqueClients)
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

        currentEntries = Mathf.Min(currentEntries, TargetValue);
        NotifyProgressChanged();

        if (currentEntries >= TargetValue)
        {
            Complete(actorClientId);
        }
    }
}