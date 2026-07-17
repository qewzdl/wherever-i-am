using UnityEngine;

[CreateAssetMenu(menuName = "Wherever I Am/Network/Connection Approval Config", fileName = "NetworkConnectionApprovalConfig")]
public sealed class NetworkConnectionApprovalConfig : ScriptableObject
{
    [Header("Remote Clients")]
    [SerializeField] private GameState remoteClientAllowedState;
    [SerializeField] private bool allowInGameLateJoin;
    [SerializeField] private string remoteClientDeniedReason;

    public GameState RemoteClientAllowedState => remoteClientAllowedState;
    public bool AllowInGameLateJoin => allowInGameLateJoin;
    public string RemoteClientDeniedReason => remoteClientDeniedReason;

    public bool CanAcceptRemoteClient(GameState currentState)
    {
        return currentState == remoteClientAllowedState ||
               (allowInGameLateJoin && currentState == GameState.InGame);
    }

    public bool Validate(Object context)
    {
        if (!string.IsNullOrWhiteSpace(remoteClientDeniedReason))
            return true;

        Debug.LogError($"{nameof(NetworkConnectionApprovalConfig)} is missing remote client denied reason.", context);
        return false;
    }
}
