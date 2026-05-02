using TMPro;
using UnityEngine;

public class MainMenuUI : MonoBehaviour
{
    [Header("Join")]
    [SerializeField] private TMP_InputField ipInputField;

    [Header("Error UI")]
    [SerializeField] private GameObject errorPanel;
    [SerializeField] private TMP_Text errorText;

    private INetworkSessionService sessionService;

    public void Construct(INetworkSessionService sessionService)
    {
        this.sessionService = sessionService;
        ShowLastConnectionErrorIfNeeded();
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

        if (ipInputField == null || string.IsNullOrWhiteSpace(ipInputField.text))
        {
            ShowError("IP address is empty.");
            return;
        }

        await sessionService.JoinLanAsync(ipInputField.text);
    }

    public void OnQuitButtonClicked()
    {
        Application.Quit();
    }

    public void HideError()
    {
        if (errorPanel != null)
            errorPanel.SetActive(false);
    }

    private bool HasSessionService()
    {
        if (sessionService != null)
            return true;

        ShowError("Network session service is missing.");
        return false;
    }

    private void ShowLastConnectionErrorIfNeeded()
    {
        if (!HasSessionService())
        {
            HideError();
            return;
        }

        if (!sessionService.HasLastError)
        {
            HideError();
            return;
        }

        ShowError(sessionService.LastErrorMessage);
        sessionService.ClearLastError();
    }

    private void ShowError(string message)
    {
        if (errorPanel != null)
            errorPanel.SetActive(true);

        if (errorText != null)
            errorText.text = message;
    }
}