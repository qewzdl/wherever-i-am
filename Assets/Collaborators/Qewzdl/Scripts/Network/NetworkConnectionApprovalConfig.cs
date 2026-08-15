using UnityEngine;

[CreateAssetMenu(menuName = "Wherever I Am/Network/Connection Approval Config", fileName = "NetworkConnectionApprovalConfig")]
public sealed class NetworkConnectionApprovalConfig : ScriptableObject
{
    [Header("Remote Clients")]
    [SerializeField] private GameState remoteClientAllowedState;
    [SerializeField] private bool allowInGameLateJoin;
    [SerializeField] private string remoteClientDeniedReason;

    [Header("Reconnect")]
    [SerializeField, Min(0f)] private float reconnectGracePeriodSeconds = 20f;

    [Header("Denial Reasons")]
    [SerializeField] private string invalidPayloadReason =
        "The connection request is invalid.";
    [SerializeField] private string incompatibleBuildReason =
        "The client build is incompatible with the host.";
    [SerializeField] private string sessionFullReason =
        "The network session is full.";
    [SerializeField] private string duplicatePlayerReason =
        "This player is already connected.";
    [SerializeField] private string lobbyPrivateReason =
        "The lobby is private.";

    public GameState RemoteClientAllowedState => remoteClientAllowedState;
    public bool AllowInGameLateJoin => allowInGameLateJoin;
    public string RemoteClientDeniedReason => remoteClientDeniedReason;
    public float ReconnectGracePeriodSeconds =>
        Mathf.Max(0f, reconnectGracePeriodSeconds);
    public string InvalidPayloadReason => invalidPayloadReason;
    public string IncompatibleBuildReason => incompatibleBuildReason;
    public string SessionFullReason => sessionFullReason;
    public string DuplicatePlayerReason => duplicatePlayerReason;
    public string LobbyPrivateReason => lobbyPrivateReason;

    public bool CanAcceptRemoteClient(GameState currentState)
    {
        return currentState == remoteClientAllowedState ||
               (allowInGameLateJoin && currentState == GameState.InGame);
    }

    public bool Validate(Object context)
    {
        bool valid = true;

        valid &= ValidateReason(
            remoteClientDeniedReason,
            nameof(remoteClientDeniedReason),
            context);
        valid &= ValidateReason(
            invalidPayloadReason,
            nameof(invalidPayloadReason),
            context);
        valid &= ValidateReason(
            incompatibleBuildReason,
            nameof(incompatibleBuildReason),
            context);
        valid &= ValidateReason(
            sessionFullReason,
            nameof(sessionFullReason),
            context);
        valid &= ValidateReason(
            duplicatePlayerReason,
            nameof(duplicatePlayerReason),
            context);
        valid &= ValidateReason(
            lobbyPrivateReason,
            nameof(lobbyPrivateReason),
            context);

        return valid;
    }

    private static bool ValidateReason(
        string reason,
        string fieldName,
        Object context)
    {
        if (!string.IsNullOrWhiteSpace(reason))
            return true;

        Debug.LogError(
            $"{nameof(NetworkConnectionApprovalConfig)} is missing " +
            $"'{fieldName}'.",
            context);
        return false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        reconnectGracePeriodSeconds = Mathf.Max(
            0f,
            reconnectGracePeriodSeconds);
    }
#endif
}
