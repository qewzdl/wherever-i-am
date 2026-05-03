using TMPro;
using UnityEngine;

public class MainMenuUI : MonoBehaviour
{
    [Header("Join")]
    [SerializeField] private TMP_InputField ipInputField;

    private INetworkSessionService sessionService;
    private IUiErrorService errorService;

    public void Construct(INetworkSessionService sessionService, IUiErrorService errorService)
    {
        this.sessionService = sessionService;
        this.errorService = errorService;
    }

    public async void OnCreateLobbyButtonClicked()
    {
        HideError();

        if (!HasSessionService())
            return;

        await sessionService.HostLanAsync();
    }

    public async void OnJoinLobbyButtonClicked()
    {
        HideError();

        if (!HasSessionService())
            return;

        string ip = ipInputField != null
            ? ipInputField.text
            : string.Empty;

        await sessionService.JoinLanAsync(ip);
    }

    public void OnQuitButtonClicked()
    {
        Application.Quit();
    }

    public void HideError()
    {
        if (TryGetErrorService(out IUiErrorService service))
            service.HideError();
    }

    private bool HasSessionService()
    {
        if (sessionService != null)
            return true;

        ShowError("Network session service is missing.");
        return false;
    }

    private void ShowError(string message)
    {
        if (TryGetErrorService(out IUiErrorService service))
            service.ShowError(message);
    }

    private bool TryGetErrorService(out IUiErrorService service)
    {
        service = errorService;

        if (service != null)
            return true;

        if (!UiErrorManager.TryGetInstance(out UiErrorManager uiErrorManager))
            return false;

        errorService = uiErrorManager;
        service = errorService;

        return true;
    }
}
