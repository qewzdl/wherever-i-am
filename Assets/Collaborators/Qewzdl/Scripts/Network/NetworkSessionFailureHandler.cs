using System.Threading.Tasks;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkSessionFailureHandler : MonoBehaviour
{
    [SerializeField] private NetworkSessionShutdownCoordinator shutdownCoordinator;

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

        _ = FailAndReturnToMainMenuAsync(result);
    }

    public void FailAndReturnToMainMenu(ConnectionResult result)
    {
        _ = FailAndReturnToMainMenuAsync(result);
    }

    public Task FailAndReturnToMainMenuAsync(ConnectionResult result)
    {
        if (!HasRequiredReferences())
            return Task.CompletedTask;

        return shutdownCoordinator.ShutdownAndWaitAsync(result);
    }

    private bool HasRequiredReferences()
    {
        ResolveReferences();

        return ValidateRequiredReference(shutdownCoordinator, nameof(shutdownCoordinator));
    }

    private bool ValidateRequiredReference(Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError($"{nameof(NetworkSessionFailureHandler)} is missing '{fieldName}'.", this);
        return false;
    }

    private void ResolveReferences()
    {
        if (shutdownCoordinator == null)
            shutdownCoordinator = GetComponent<NetworkSessionShutdownCoordinator>();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveReferences();
    }
#endif
}
