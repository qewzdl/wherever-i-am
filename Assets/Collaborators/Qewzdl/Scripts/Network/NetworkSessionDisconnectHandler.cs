using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkSessionDisconnectHandler : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private GameStateMachine stateMachine;
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
        networkCallbacksSubscribed = true;
    }

    public void StopListening()
    {
        if (!networkCallbacksSubscribed)
            return;

        if (subscribedNetworkManager != null)
            subscribedNetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;

        subscribedNetworkManager = null;
        networkCallbacksSubscribed = false;
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (!HasRequiredReferences())
            return;

        if (clientId != networkManager.LocalClientId)
            return;

        if (stateMachine.CurrentState == GameState.Disconnecting)
            return;

        if (stateMachine.CurrentState == GameState.Connecting)
        {
            StopListening();
            failureHandler.FailAndReturnToMainMenu("Connection failed or was interrupted while connecting.");
            return;
        }

        if (stateMachine.CurrentState == GameState.Lobby ||
            stateMachine.CurrentState == GameState.LoadingGame ||
            stateMachine.CurrentState == GameState.InGame)
        {
            StopListening();
            failureHandler.FailAndReturnToMainMenu("Disconnected from network session.");
        }
    }

    private bool HasRequiredReferences()
    {
        bool valid = true;

        valid &= ValidateRequiredReference(networkManager, nameof(networkManager));
        valid &= ValidateRequiredReference(stateMachine, nameof(stateMachine));
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