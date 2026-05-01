using TMPro;
using UnityEngine;

public class MainMenuUI : MonoBehaviour
{
    [Header("Join")]
    [SerializeField] private TMP_InputField ipInputField;

    [Header("Error UI")]
    [SerializeField] private GameObject errorPanel;
    [SerializeField] private TMP_Text errorText;

    private void Start()
    {
        ShowLastConnectionErrorIfNeeded();
    }

    public async void OnCreateLobbyButtonClicked()
    {
        HideError();

        if (NetworkSessionOrchestrator.Instance == null)
        {
            ShowError("Network session orchestrator is missing.");
            return;
        }

        await NetworkSessionOrchestrator.Instance.HostLanAsync();
    }

    public async void OnJoinLobbyButtonClicked()
    {
        HideError();

        if (NetworkSessionOrchestrator.Instance == null)
        {
            ShowError("Network session orchestrator is missing.");
            return;
        }

        await NetworkSessionOrchestrator.Instance.JoinLanAsync(ipInputField.text);
    }

    public void OnQuitButtonClicked()
    {
        Application.Quit();
    }

    public void HideError()
    {
        if (errorPanel != null) errorPanel.SetActive(false);
    }

    private void ShowLastConnectionErrorIfNeeded()
    {
        if (NetworkSessionOrchestrator.Instance == null)
        {
            HideError();
            return;
        }

        if (!NetworkSessionOrchestrator.Instance.HasLastError)
        {
            HideError();
            return;
        }

        ShowError(NetworkSessionOrchestrator.Instance.LastErrorMessage);
        NetworkSessionOrchestrator.Instance.ClearLastError();
    }

    private void ShowError(string message)
    {
        if (errorPanel != null) errorPanel.SetActive(true);

        if (errorText != null) errorText.text = message;
    }
}