using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkConnectionApprovalService : MonoBehaviour
{
    private const string LobbyJoinDeniedReason = "The game has already started. You can only join while the host is in the lobby.";

    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private GameStateMachine stateMachine;

    private NetworkManager configuredNetworkManager;
    private bool approvalConfigured;

    private void Awake()
    {
        Configure();
    }

    private void OnEnable()
    {
        Configure();
    }

    private void OnDisable()
    {
        ClearConfiguration();
    }

    public void Configure()
    {
        if (!HasRequiredReferences())
            return;

        if (networkManager.IsListening)
            return;

        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.ConnectionApprovalCallback = HandleConnectionApproval;

        configuredNetworkManager = networkManager;
        approvalConfigured = true;
    }

    private void ClearConfiguration()
    {
        if (!approvalConfigured)
            return;

        if (configuredNetworkManager == null)
        {
            approvalConfigured = false;
            return;
        }

        if (configuredNetworkManager.IsListening)
            return;

        configuredNetworkManager.ConnectionApprovalCallback = null;
        configuredNetworkManager = null;
        approvalConfigured = false;
    }

    private void HandleConnectionApproval(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        response.CreatePlayerObject = false;
        response.Pending = false;

        if (request.ClientNetworkId == NetworkManager.ServerClientId)
        {
            response.Approved = true;
            return;
        }

        if (CanAcceptRemoteClientConnection())
        {
            response.Approved = true;
            return;
        }

        response.Approved = false;
        response.Reason = LobbyJoinDeniedReason;

        Debug.Log($"Rejected client {request.ClientNetworkId}: {LobbyJoinDeniedReason}");
    }

    private bool CanAcceptRemoteClientConnection()
    {
        return stateMachine.CurrentState == GameState.Lobby;
    }

    private bool HasRequiredReferences()
    {
        if (networkManager == null)
        {
            Debug.LogError($"{nameof(NetworkConnectionApprovalService)} is missing {nameof(NetworkManager)}.", this);
            return false;
        }

        if (stateMachine == null)
        {
            Debug.LogError($"{nameof(NetworkConnectionApprovalService)} is missing {nameof(GameStateMachine)}.", this);
            return false;
        }

        return true;
    }
}