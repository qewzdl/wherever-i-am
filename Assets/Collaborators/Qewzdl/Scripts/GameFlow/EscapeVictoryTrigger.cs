using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider))]
public class EscapeVictoryTrigger : NetworkBehaviour
{
    [SerializeField] private NetworkGameOutcome gameOutcome;
    [SerializeField] private bool disableAfterVictory = true;

    private Collider triggerCollider;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider>();

        if (!triggerCollider.isTrigger)
        {
            Debug.LogError($"{nameof(EscapeVictoryTrigger)} requires Collider with Is Trigger enabled.", this);
            enabled = false;
            return;
        }

        if (gameOutcome == null)
        {
            Debug.LogError($"{nameof(EscapeVictoryTrigger)} requires assigned {nameof(NetworkGameOutcome)}.", this);
            enabled = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer)
            return;

        if (gameOutcome == null)
        {
            Debug.LogError($"{nameof(EscapeVictoryTrigger)} lost {nameof(NetworkGameOutcome)} reference.", this);
            enabled = false;
            return;
        }

        if (gameOutcome.CurrentState != GameOutcomeState.Running)
            return;

        NetworkObject playerNetworkObject = ResolvePlayerNetworkObject(other);

        if (playerNetworkObject == null)
            return;

        bool victoryDeclared = gameOutcome.TryRegisterPlayerEscapeServer(playerNetworkObject);

        if (victoryDeclared && disableAfterVictory)
            enabled = false;
    }

    private NetworkObject ResolvePlayerNetworkObject(Collider other)
    {
        if (other == null)
            return null;

        if (other.attachedRigidbody != null)
        {
            NetworkObject rigidbodyNetworkObject = other.attachedRigidbody.GetComponentInParent<NetworkObject>();

            if (rigidbodyNetworkObject != null)
                return rigidbodyNetworkObject;
        }

        return other.GetComponentInParent<NetworkObject>();
    }
}