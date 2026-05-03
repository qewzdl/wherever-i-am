using UnityEngine;

public class MainMenuCompositionRoot : CompositionRoot
{
    [Header("Session")]
    [SerializeField] private NetworkSessionOrchestrator sessionService;

    [Header("UI")]
    [SerializeField] private MainMenuUI mainMenuUI;

    private IUiErrorService errorService;

    protected override void ResolveReferences()
    {
        if (sessionService == null)
            sessionService = NetworkSessionOrchestrator.Instance != null
                ? NetworkSessionOrchestrator.Instance
                : FindFirstObjectByType<NetworkSessionOrchestrator>();

        if (mainMenuUI == null)
            mainMenuUI = FindFirstObjectByType<MainMenuUI>();

        if (errorService == null)
        {
            if (UiErrorManager.TryGetInstance(out UiErrorManager uiErrorManager))
                errorService = uiErrorManager;
        }
    }

    protected override void Compose()
    {
        if (sessionService == null)
        {
            Debug.LogError("NetworkSessionOrchestrator was not found.");
            return;
        }

        if (mainMenuUI == null)
        {
            Debug.LogError("MainMenuUI was not found.");
            return;
        }

        mainMenuUI.Construct(sessionService, errorService);
    }
}
