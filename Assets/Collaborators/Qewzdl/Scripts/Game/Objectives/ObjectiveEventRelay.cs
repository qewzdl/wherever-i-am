using Unity.Netcode;
using UnityEngine;

public sealed class ObjectiveEventRelay : NetworkBehaviour
{
    [Header("Required")]
    [SerializeField] private GameplayEventHub gameplayEventHub;

    [Header("Event")]
    [SerializeField] private string eventId = "objective.completed";
    [SerializeField] private bool includeOwnerAsActor = true;

    public void RaiseServerEvent()
    {
        if (!IsSpawned || !IsServer)
        {
            Debug.LogError($"{nameof(ObjectiveEventRelay)} can raise objective event only on server.", this);
            return;
        }

        if (gameplayEventHub == null)
        {
            Debug.LogError($"{nameof(ObjectiveEventRelay)} requires {nameof(GameplayEventHub)} reference.", this);
            return;
        }

        ulong actorClientId = includeOwnerAsActor ? OwnerClientId : 0;
        gameplayEventHub.TryRaiseServerEvent(eventId, actorClientId, NetworkObject);
    }
}