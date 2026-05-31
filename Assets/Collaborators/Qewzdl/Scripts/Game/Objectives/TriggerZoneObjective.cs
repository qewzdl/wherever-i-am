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

    private void OnTriggerEnter(Collider other)
    {
        if (!CanRunServerLogic())
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(requiredTag) && !other.CompareTag(requiredTag))
        {
            return;
        }

        NetworkObject networkObject = other.GetComponentInParent<NetworkObject>();

        if (networkObject == null)
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