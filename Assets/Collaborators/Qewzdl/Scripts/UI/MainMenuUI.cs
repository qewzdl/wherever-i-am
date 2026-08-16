using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    [Header("Join")]
    [SerializeField] private TMP_InputField ipInputField;

    [Header("While connecting")]
    [SerializeField] private GameObject busyPanel;
    [SerializeField] private TMP_Text busyText;

    // Named rather than found: the buttons call this class through UnityEvents
    // and it holds no reference to them otherwise. Without this the guard flag
    // still swallows the second click, which is what makes the menu look dead.
    [SerializeField] private Selectable[] disableWhileBusy;

    [SerializeField] private string hostingMessage = "Creating lobby...";
    [SerializeField] private string joiningMessage = "Connecting to {0}...";

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
        EndRequest();
    }

    private void Awake()
    {
        SetBusy(false, string.Empty);
    }

    public async void OnCreateLobbyButtonClicked()
    {
        if (!TryBeginRequest(hostingMessage))
            return;

        try
        {
            if (!HasSessionService())
                return;

            await sessionService.HostLanAsync();
        }
        finally
        {
            EndRequest();
        }
    }

    public async void OnJoinLobbyButtonClicked()
    {
        string ip = ipInputField != null
            ? ipInputField.text
            : string.Empty;

        if (!TryBeginRequest(string.Format(joiningMessage, ip)))
            return;

        try
        {
            if (!HasSessionService())
                return;

            await sessionService.JoinLanAsync(ip);
        }
        finally
        {
            EndRequest();
        }
    }

    // Released in a finally, so a service that throws leaves the menu usable
    // rather than dead until the scene reloads.
    private bool TryBeginRequest(string busyMessage)
    {
        if (isRequestInFlight)
            return false;

        isRequestInFlight = true;
        HideError();
        SetBusy(true, busyMessage);
        return true;
    }

    private void EndRequest()
    {
        isRequestInFlight = false;
        SetBusy(false, string.Empty);
    }

    private void SetBusy(bool isBusy, string message)
    {
        if (busyPanel != null)
            busyPanel.SetActive(isBusy);

        if (busyText != null && isBusy)
            busyText.text = message;

        if (disableWhileBusy == null)
            return;

        for (int i = 0; i < disableWhileBusy.Length; i++)
        {
            if (disableWhileBusy[i] != null)
                disableWhileBusy[i].interactable = !isBusy;
        }
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
