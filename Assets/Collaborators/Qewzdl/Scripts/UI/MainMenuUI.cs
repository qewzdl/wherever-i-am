using TMPro;
using UnityEngine;

public class MainMenuUI : MonoBehaviour
{
    [Header("Join")]
    [SerializeField] private TMP_InputField ipInputField;

    private INetworkSessionService sessionService;
    private IUiErrorService errorService;

    // A Unity button needs async void, and async void hands the click straight
    // back the moment the first await is reached. The session service does
    // refuse the second attempt, but only after it has been made - and on a
    // join that is timing out, that is a screenful of errors for a player
    // doing the obvious thing and clicking again. Hosting and joining share
    // the flag: both end in one session, and starting one while the other is
    // in flight is the same mistake.
    private bool isRequestInFlight;

    public bool IsRequestInFlight => isRequestInFlight;

    public void Construct(INetworkSessionService sessionService, IUiErrorService errorService)
    {
        this.sessionService = sessionService;
        this.errorService = errorService;
    }

    public void Dispose()
    {
        sessionService = null;
        errorService = null;
        isRequestInFlight = false;
    }

    public async void OnCreateLobbyButtonClicked()
    {
        if (!TryBeginRequest())
            return;

        try
        {
            if (!HasSessionService())
                return;

            await sessionService.HostLanAsync();
        }
        finally
        {
            isRequestInFlight = false;
        }
    }

    public async void OnJoinLobbyButtonClicked()
    {
        if (!TryBeginRequest())
            return;

        try
        {
            if (!HasSessionService())
                return;

            string ip = ipInputField != null
                ? ipInputField.text
                : string.Empty;

            await sessionService.JoinLanAsync(ip);
        }
        finally
        {
            isRequestInFlight = false;
        }
    }

    // Released in a finally, so a service that throws leaves the menu usable
    // rather than dead until the scene reloads.
    private bool TryBeginRequest()
    {
        if (isRequestInFlight)
            return false;

        isRequestInFlight = true;
        HideError();
        return true;
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
        return service != null;
    }
}
