using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public sealed class TriggerZoneObjective : ObjectiveCondition
{
    [Header("Zone")]
    [SerializeField] private string requiredTag = "Player";
    [SerializeField] private int requiredEntries = 1;
    [SerializeField] private bool countUniqueClients = true;

    private readonly HashSet<ulong> enteredClientIds = new HashSet<ulong>();
    private int currentEntries;

    public override int CurrentValue => currentEntries;
    public override int TargetValue => Mathf.Max(1, requiredEntries);

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

        if (!string.IsNullOrWhiteSpace(requiredTag) && !other.CompareTag(requiredTag))
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

        currentEntries = Mathf.Min(currentEntries, TargetValue);
        NotifyProgressChanged();

        if (currentEntries >= TargetValue)
        {
            Complete(actorClientId);
        }
    }
}