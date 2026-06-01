using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class EscapeObjective : ObjectiveCondition
{
    [Header("Escape")]
    [SerializeField] private EscapeObjectiveMode escapeMode = EscapeObjectiveMode.AnyPlayerEscapes;
    [SerializeField] private string requiredTag = "Player";
    [SerializeField] private bool disableColliderAfterCompletion = true;

    private readonly HashSet<ulong> escapedClientIds = new HashSet<ulong>();

    private Collider triggerCollider;
    private NetworkManager registeredNetworkManager;
    private int requiredEscapedPlayersCount = 1;

    public override int CurrentValue => escapedClientIds.Count;
    public override int TargetValue => Mathf.Max(1, requiredEscapedPlayersCount);

    private void Awake()
    {
        triggerCollider = GetComponent<Collider>();

        if (triggerCollider == null)
        {
            Debug.LogError($"{nameof(EscapeObjective)} requires {nameof(Collider)}.", this);
            enabled = false;
            return;
        }

        if (!triggerCollider.isTrigger)
        {
            Debug.LogError($"{nameof(EscapeObjective)} requires Collider with Is Trigger enabled.", this);
            enabled = false;
        }
    }

    private void Reset()
    {
        triggerCollider = GetComponent<Collider>();

        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
        }
    }

    private void OnValidate()
    {
        if (triggerCollider == null)
        {
            triggerCollider = GetComponent<Collider>();
        }
    }

    protected override void OnObjectiveStarted()
    {
        escapedClientIds.Clear();

        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null)
        {
            Debug.LogError($"{nameof(EscapeObjective)} requires active {nameof(NetworkManager)}.", this);
            enabled = false;
            return;
        }

        if (triggerCollider == null || !triggerCollider.isTrigger)
        {
            Debug.LogError($"{nameof(EscapeObjective)} requires valid trigger collider.", this);
            enabled = false;
            return;
        }

        if (disableColliderAfterCompletion)
        {
            triggerCollider.enabled = true;
        }

        RegisterNetworkCallbacks(networkManager);
        RefreshRequiredEscapedPlayersCountServer(networkManager);
    }

    protected override void OnObjectiveStopped()
    {
        escapedClientIds.Clear();
        UnregisterNetworkCallbacks();
    }

    protected override void OnObjectiveCompleted()
    {
        UnregisterNetworkCallbacks();

        if (disableColliderAfterCompletion && triggerCollider != null)
        {
            triggerCollider.enabled = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!CanRunServerLogic() || !IsRunning || IsCompleted)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(requiredTag) && !other.CompareTag(requiredTag))
        {
            return;
        }

        NetworkObject playerNetworkObject = ResolvePlayerNetworkObject(other);

        if (playerNetworkObject == null)
        {
            return;
        }

        if (!playerNetworkObject.IsSpawned || !playerNetworkObject.IsPlayerObject)
        {
            return;
        }

        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null)
        {
            Debug.LogError($"{nameof(EscapeObjective)} requires active {nameof(NetworkManager)}.", this);
            enabled = false;
            return;
        }

        ulong clientId = playerNetworkObject.OwnerClientId;

        if (!IsConnectedClientServer(networkManager, clientId))
        {
            return;
        }

        if (!escapedClientIds.Add(clientId))
        {
            return;
        }

        RefreshRequiredEscapedPlayersCountServer(networkManager);
        NotifyProgressChanged();
        TryCompleteIfSatisfied(clientId);
    }

    private void HandleClientConnectedServer(ulong clientId)
    {
        if (!CanRunServerLogic() || !IsRunning || IsCompleted)
        {
            return;
        }

        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null)
        {
            Debug.LogError($"{nameof(EscapeObjective)} requires active {nameof(NetworkManager)}.", this);
            enabled = false;
            return;
        }

        RefreshRequiredEscapedPlayersCountServer(networkManager);
        NotifyProgressChanged();
    }

    private void HandleClientDisconnectedServer(ulong clientId)
    {
        if (!CanRunServerLogic() || !IsRunning || IsCompleted)
        {
            return;
        }

        escapedClientIds.Remove(clientId);

        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null)
        {
            Debug.LogError($"{nameof(EscapeObjective)} requires active {nameof(NetworkManager)}.", this);
            enabled = false;
            return;
        }

        RefreshRequiredEscapedPlayersCountServer(networkManager);
        NotifyProgressChanged();
        TryCompleteIfSatisfied(0);
    }

    private void TryCompleteIfSatisfied(ulong instigatorClientId)
    {
        if (escapedClientIds.Count < TargetValue)
        {
            return;
        }

        Complete(instigatorClientId);
    }

    private void RefreshRequiredEscapedPlayersCountServer(NetworkManager networkManager)
    {
        if (escapeMode == EscapeObjectiveMode.AnyPlayerEscapes)
        {
            requiredEscapedPlayersCount = 1;
            return;
        }

        requiredEscapedPlayersCount = Mathf.Max(1, networkManager.ConnectedClientsIds.Count);
    }

    private void RegisterNetworkCallbacks(NetworkManager networkManager)
    {
        if (registeredNetworkManager == networkManager)
        {
            return;
        }

        UnregisterNetworkCallbacks();

        registeredNetworkManager = networkManager;
        registeredNetworkManager.OnClientConnectedCallback += HandleClientConnectedServer;
        registeredNetworkManager.OnClientDisconnectCallback += HandleClientDisconnectedServer;
    }

    private void UnregisterNetworkCallbacks()
    {
        if (registeredNetworkManager == null)
        {
            return;
        }

        registeredNetworkManager.OnClientConnectedCallback -= HandleClientConnectedServer;
        registeredNetworkManager.OnClientDisconnectCallback -= HandleClientDisconnectedServer;
        registeredNetworkManager = null;
    }

    private bool IsConnectedClientServer(NetworkManager networkManager, ulong clientId)
    {
        IReadOnlyList<ulong> connectedClientIds = networkManager.ConnectedClientsIds;

        for (int i = 0; i < connectedClientIds.Count; i++)
        {
            if (connectedClientIds[i] == clientId)
            {
                return true;
            }
        }

        return false;
    }

    private NetworkObject ResolvePlayerNetworkObject(Collider other)
    {
        if (other == null)
        {
            return null;
        }

        if (other.attachedRigidbody != null)
        {
            NetworkObject rigidbodyNetworkObject = other.attachedRigidbody.GetComponentInParent<NetworkObject>();

            if (rigidbodyNetworkObject != null)
            {
                return rigidbodyNetworkObject;
            }
        }

        return other.GetComponentInParent<NetworkObject>();
    }
}