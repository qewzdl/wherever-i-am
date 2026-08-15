using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkConnectionApprovalService : MonoBehaviour,
    INetworkSessionAdmissionService
{
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private GameStateMachine stateMachine;

    [Header("Configuration")]
    [SerializeField] private NetworkConnectionConfig connectionConfig;
    [SerializeField] private NetworkConnectionApprovalConfig approvalConfig;
    [SerializeField] private LobbyConfig lobbyConfig;

    private NetworkManager configuredNetworkManager;
    private NetworkManager subscribedNetworkManager;
    private NetworkSessionAdmissionRegistry admissionRegistry;
    private bool approvalConfigured;
    private bool networkCallbacksSubscribed;

    // A session starts private; the lobby opens it to strangers on request.
    private bool acceptingNewPlayers;

    private void Awake()
    {
        Configure();
    }

    private void OnEnable()
    {
        Configure();
        SubscribeToNetworkCallbacks();
    }

    private void OnDisable()
    {
        UnsubscribeFromNetworkCallbacks();
        ClearConfiguration();

        if (networkManager == null || !networkManager.IsListening)
            admissionRegistry?.Reset();
    }

    public void Configure()
    {
        if (!HasRequiredReferences())
            return;

        EnsureAdmissionRegistry();

        if (networkManager.IsListening)
            return;

        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.ConnectionApprovalCallback = HandleConnectionApproval;

        configuredNetworkManager = networkManager;
        approvalConfigured = true;
    }

    public bool TryGetPlayerId(ulong clientId, out string playerId)
    {
        if (admissionRegistry != null)
            return admissionRegistry.TryGetPlayerId(clientId, out playerId);

        playerId = string.Empty;
        return false;
    }

    public bool IsReconnect(ulong clientId)
    {
        return admissionRegistry != null &&
               admissionRegistry.IsReconnect(clientId);
    }

    public bool HasReconnectReservation(string playerId)
    {
        return admissionRegistry != null &&
               admissionRegistry.HasReconnectReservation(playerId);
    }

    public void SetAcceptingNewPlayers(bool accepting)
    {
        acceptingNewPlayers = accepting;
    }

    public void RecordDisconnect(ulong clientId)
    {
        if (admissionRegistry == null)
            return;

        admissionRegistry.RecordDisconnect(
            clientId,
            ShouldReserveDisconnectedPlayer());
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

        if (!NetworkConnectionPayloadCodec.TryDecode(
                request.Payload,
                out NetworkConnectionPayload payload,
                out string payloadError))
        {
            Reject(
                request.ClientNetworkId,
                response,
                approvalConfig.InvalidPayloadReason,
                payloadError);
            return;
        }

        bool isHostPlayer =
            request.ClientNetworkId == NetworkManager.ServerClientId;

        if (!isHostPlayer && !CanAcceptRemoteClientConnection())
        {
            Reject(
                request.ClientNetworkId,
                response,
                approvalConfig.RemoteClientDeniedReason,
                $"Game state is {stateMachine.CurrentState}.");
            return;
        }

        EnsureAdmissionRegistry();

        // A private lobby turns away newcomers only. Someone who dropped still
        // holds a seat here, and taking it from them would make going private
        // a punishment for a lost connection.
        if (!isHostPlayer &&
            !acceptingNewPlayers &&
            !admissionRegistry.HasReconnectReservation(payload.PlayerId))
        {
            Reject(
                request.ClientNetworkId,
                response,
                approvalConfig.LobbyPrivateReason,
                "Lobby is private.");
            return;
        }

        NetworkAdmissionResult result = admissionRegistry.TryAdmit(
            request.ClientNetworkId,
            payload);

        if (result.Accepted)
        {
            response.Approved = true;
            RuntimeLog.Info(
                result.IsReconnect
                    ? $"Reconnected client {request.ClientNetworkId}."
                    : $"Admitted client {request.ClientNetworkId}.");
            return;
        }

        string userReason = GetDenialReason(result.Status);
        Reject(
            request.ClientNetworkId,
            response,
            userReason,
            $"Admission status: {result.Status}.");
    }

    private void Reject(
        ulong clientId,
        NetworkManager.ConnectionApprovalResponse response,
        string userReason,
        string debugReason)
    {
        response.Approved = false;
        response.Reason = userReason;

        RuntimeLog.Info(
            $"Rejected client {clientId}: {debugReason} " +
            $"User reason: {userReason}");
    }

    private string GetDenialReason(NetworkAdmissionStatus status)
    {
        return status switch
        {
            NetworkAdmissionStatus.ProtocolMismatch =>
                approvalConfig.IncompatibleBuildReason,
            NetworkAdmissionStatus.BuildMismatch =>
                approvalConfig.IncompatibleBuildReason,
            NetworkAdmissionStatus.DuplicatePlayer =>
                approvalConfig.DuplicatePlayerReason,
            NetworkAdmissionStatus.SessionFull =>
                approvalConfig.SessionFullReason,
            _ => approvalConfig.InvalidPayloadReason
        };
    }

    private bool CanAcceptRemoteClientConnection()
    {
        return approvalConfig.CanAcceptRemoteClient(stateMachine.CurrentState);
    }

    private bool ShouldReserveDisconnectedPlayer()
    {
        if (networkManager == null ||
            !networkManager.IsServer ||
            networkManager.ShutdownInProgress)
        {
            return false;
        }

        GameState currentState = stateMachine.CurrentState;
        return currentState == GameState.Lobby ||
               currentState == GameState.LoadingGame ||
               currentState == GameState.InGame;
    }

    private void EnsureAdmissionRegistry()
    {
        if (admissionRegistry != null)
            return;

        admissionRegistry = new NetworkSessionAdmissionRegistry(
            lobbyConfig.MaxPlayers,
            connectionConfig.ProtocolVersion,
            Application.version,
            approvalConfig.ReconnectGracePeriodSeconds,
            () => Time.realtimeSinceStartupAsDouble,
            IsClientStillConnected);
    }

    // NetworkManager is the authority on who is actually connected; the
    // registry only knows what the disconnect callback has told it so far.
    private bool IsClientStillConnected(ulong clientId)
    {
        return networkManager != null &&
               networkManager.IsServer &&
               networkManager.ConnectedClients.ContainsKey(clientId);
    }

    private void SubscribeToNetworkCallbacks()
    {
        if (networkManager == null)
            return;

        if (networkCallbacksSubscribed &&
            subscribedNetworkManager == networkManager)
        {
            return;
        }

        UnsubscribeFromNetworkCallbacks();
        subscribedNetworkManager = networkManager;
        subscribedNetworkManager.OnClientDisconnectCallback +=
            HandleClientDisconnected;
        subscribedNetworkManager.OnServerStopped += HandleServerStopped;
        networkCallbacksSubscribed = true;
    }

    private void UnsubscribeFromNetworkCallbacks()
    {
        if (!networkCallbacksSubscribed)
            return;

        if (subscribedNetworkManager != null)
        {
            subscribedNetworkManager.OnClientDisconnectCallback -=
                HandleClientDisconnected;
            subscribedNetworkManager.OnServerStopped -= HandleServerStopped;
        }

        subscribedNetworkManager = null;
        networkCallbacksSubscribed = false;
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        RecordDisconnect(clientId);
    }

    private void HandleServerStopped(bool wasHost)
    {
        admissionRegistry?.Reset();
        acceptingNewPlayers = false;
    }

    private bool HasRequiredReferences()
    {
        bool valid = true;

        valid &= ValidateRequiredReference(networkManager, nameof(networkManager));
        valid &= ValidateRequiredReference(stateMachine, nameof(stateMachine));
        valid &= ValidateRequiredReference(connectionConfig, nameof(connectionConfig));
        valid &= ValidateRequiredReference(approvalConfig, nameof(approvalConfig));
        valid &= ValidateRequiredReference(lobbyConfig, nameof(lobbyConfig));

        if (connectionConfig != null)
            valid &= connectionConfig.Validate(this);

        if (approvalConfig != null)
            valid &= approvalConfig.Validate(this);

        if (string.IsNullOrWhiteSpace(Application.version))
        {
            Debug.LogError("Application build version is missing.", this);
            valid = false;
        }

        return valid;
    }

    private bool ValidateRequiredReference(Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError(
            $"{nameof(NetworkConnectionApprovalService)} is missing " +
            $"'{fieldName}'.",
            this);
        return false;
    }
}
