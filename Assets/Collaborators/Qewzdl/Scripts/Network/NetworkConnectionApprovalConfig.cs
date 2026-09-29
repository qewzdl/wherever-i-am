using UnityEngine;

[CreateAssetMenu(menuName = "Wherever I Am/Network/Connection Approval Config", fileName = "NetworkConnectionApprovalConfig")]
public sealed class NetworkConnectionApprovalConfig : ScriptableObject
{
    [Header("Remote Clients")]
    [SerializeField] private GameState remoteClientAllowedState;
    [SerializeField] private string remoteClientDeniedReason;

    // Nobody comes in once the match has begun, a player whose connection
    // dropped included. Players are made once, when the match loads, and a
    // player's body goes when they disconnect - so somebody let in mid-match
    // stood in the level with no body and nothing to watch until it ended.
    // Letting them in was once a switch here; there is nothing for it to
    // switch on until somebody can be given a body or a seat to watch from.
    [SerializeField] private string matchInProgressReason =
        "The match has already started.";

    [Header("Reconnect")]
    [SerializeField, Min(0f)] private float reconnectGracePeriodSeconds = 20f;
    [SerializeField] private string reconnectingReason =
        "Connection lost. Reconnecting...";

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
    [SerializeField] private string kickedReason =
        "The host removed you from the lobby.";

    public GameState RemoteClientAllowedState => remoteClientAllowedState;
    public string RemoteClientDeniedReason => remoteClientDeniedReason;
    public string MatchInProgressReason => matchInProgressReason;
    public float ReconnectGracePeriodSeconds =>
        Mathf.Max(0f, reconnectGracePeriodSeconds);
    public string ReconnectingReason => reconnectingReason;
    public string InvalidPayloadReason => invalidPayloadReason;
    public string IncompatibleBuildReason => incompatibleBuildReason;
    public string SessionFullReason => sessionFullReason;
    public string DuplicatePlayerReason => duplicatePlayerReason;
    public string LobbyPrivateReason => lobbyPrivateReason;
    public string KickedReason => kickedReason;

    public bool CanAcceptRemoteClient(GameState currentState)
    {
        return currentState == remoteClientAllowedState;
    }

    // What somebody turned away is told: that the match has started, if it
    // has, rather than a general "not accepting players right now" that
    // leaves them wondering whether to try again.
    public string DenialReasonFor(GameState currentState)
    {
        return currentState == GameState.LoadingGame || currentState == GameState.InGame
            ? matchInProgressReason
            : remoteClientDeniedReason;
    }

    public bool Validate(Object context)
    {
        bool valid = true;

        valid &= ValidateReason(
            remoteClientDeniedReason,
            nameof(remoteClientDeniedReason),
            context);
        valid &= ValidateReason(
            matchInProgressReason,
            nameof(matchInProgressReason),
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
        valid &= ValidateReason(
            kickedReason,
            nameof(kickedReason),
            context);
        valid &= ValidateReason(
            reconnectingReason,
            nameof(reconnectingReason),
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
