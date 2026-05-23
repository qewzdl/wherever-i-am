using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkSessionFailureHandler : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameStateMachine stateMachine;
    [SerializeField] private NetworkConnectionService connectionService;
    [SerializeField] private ProjectSceneFlowService sceneFlowService;
    [SerializeField] private UiErrorManager errorManager;

    private void Awake()
    {
        HasRequiredReferences();
    }

    public void FailAndReturnToMainMenu(string userMessage, string debugMessage = "")
    {
        ConnectionResult result = ConnectionResult.Fail(
            ConnectionErrorCode.Unknown,
            userMessage,
            string.IsNullOrWhiteSpace(debugMessage) ? userMessage : debugMessage,
            true
        );

        FailAndReturnToMainMenu(result);
    }

    public void FailAndReturnToMainMenu(ConnectionResult result)
    {
        if (!HasRequiredReferences())
            return;

        Debug.LogWarning(result.DebugMessage);

        stateMachine.ChangeState(GameState.Error);

        connectionService.Shutdown();

        if (!sceneFlowService.LoadScene(ProjectSceneKind.MainMenu))
            Debug.LogError("Failed to load main menu after network failure.", this);

        errorManager.ShowError(result.UserMessage);
    }

    private bool HasRequiredReferences()
    {
        bool valid = true;

        valid &= ValidateRequiredReference(stateMachine, nameof(stateMachine));
        valid &= ValidateRequiredReference(connectionService, nameof(connectionService));
        valid &= ValidateRequiredReference(sceneFlowService, nameof(sceneFlowService));
        valid &= ValidateRequiredReference(errorManager, nameof(errorManager));

        return valid;
    }

    private bool ValidateRequiredReference(Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError($"{nameof(NetworkSessionFailureHandler)} is missing '{fieldName}'.", this);
        return false;
    }
}