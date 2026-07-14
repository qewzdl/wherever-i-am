using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkSessionDisconnectHandler : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private NetworkSessionStateMachine sessionStateMachine;
    [SerializeField] private NetworkSessionFailureHandler failureHandler;

    private NetworkManager subscribedNetworkManager;
    private bool networkCallbacksSubscribed;

    private void Awake()
    {
        HasRequiredReferences();
    }

    private void OnDisable()
    {
        StopListening();
    }

    public void StartListening()
    {
        if (!HasRequiredReferences())
            return;

        if (networkCallbacksSubscribed && subscribedNetworkManager == networkManager)
            return;

        StopListening();

        subscribedNetworkManager = networkManager;
        subscribedNetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        subscribedNetworkManager.OnTransportFailure += HandleTransportFailure;
        networkCallbacksSubscribed = true;
    }

    public void StopListening()
    {
        if (!networkCallbacksSubscribed)
            return;

        if (subscribedNetworkManager != null)
        {
            subscribedNetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            subscribedNetworkManager.OnTransportFailure -= HandleTransportFailure;
        }

        subscribedNetworkManager = null;
        networkCallbacksSubscribed = false;
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (!HasRequiredReferences())
            return;

        if (clientId != networkManager.LocalClientId)
            return;

        NetworkSessionState state = sessionStateMachine.CurrentState;

        if (state == NetworkSessionState.Disconnecting ||
            state == NetworkSessionState.Failed ||
            state == NetworkSessionState.Offline)
        {
            return;
        }

        if (state == NetworkSessionState.StartingHost ||
            state == NetworkSessionState.StartingClient)
        {
            StopListening();
            failureHandler.FailAndReturnToMainMenu("Connection failed or was interrupted while connecting.");
            return;
        }

        if (state == NetworkSessionState.Lobby ||
            state == NetworkSessionState.LoadingGame ||
            state == NetworkSessionState.InGame)
        {
            StopListening();
            failureHandler.FailAndReturnToMainMenu("Disconnected from network session.");
        }
    }

    private void HandleTransportFailure()
    {
        if (!HasRequiredReferences())
            return;

        NetworkSessionState state = sessionStateMachine.CurrentState;

        if (state == NetworkSessionState.Disconnecting ||
            state == NetworkSessionState.Failed ||
            state == NetworkSessionState.Offline)
        {
            return;
        }

        StopListening();
        failureHandler.FailAndReturnToMainMenu("Network transport failed.");
    }

    private bool HasRequiredReferences()
    {
        bool valid = true;

        valid &= ValidateRequiredReference(networkManager, nameof(networkManager));
        valid &= ValidateRequiredReference(sessionStateMachine, nameof(sessionStateMachine));
        valid &= ValidateRequiredReference(failureHandler, nameof(failureHandler));

        return valid;
    }

    private bool ValidateRequiredReference(Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError($"{nameof(NetworkSessionDisconnectHandler)} is missing '{fieldName}'.", this);
        return false;
    }
}
