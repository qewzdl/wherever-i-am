using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkConnectionApprovalService : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private GameStateMachine stateMachine;

    [Header("Configuration")]
    [SerializeField] private NetworkConnectionApprovalConfig approvalConfig;

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
        response.Reason = approvalConfig.RemoteClientDeniedReason;

        RuntimeLog.Info($"Rejected client {request.ClientNetworkId}: {approvalConfig.RemoteClientDeniedReason}");
    }

    private bool CanAcceptRemoteClientConnection()
    {
        return approvalConfig.CanAcceptRemoteClient(stateMachine.CurrentState);
    }

    private bool HasRequiredReferences()
    {
        bool valid = true;

        valid &= ValidateRequiredReference(networkManager, nameof(networkManager));
        valid &= ValidateRequiredReference(stateMachine, nameof(stateMachine));
        valid &= ValidateRequiredReference(approvalConfig, nameof(approvalConfig));

        if (approvalConfig != null)
            valid &= approvalConfig.Validate(this);

        return valid;
    }

    private bool ValidateRequiredReference(Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError($"{nameof(NetworkConnectionApprovalService)} is missing '{fieldName}'.", this);
        return false;
    }
}
