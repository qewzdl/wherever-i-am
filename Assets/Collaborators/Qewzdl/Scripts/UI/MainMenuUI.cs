using TMPro;
using UnityEngine;

public class MainMenuUI : MonoBehaviour
{
    [Header("Join")]
    [SerializeField] private TMP_InputField ipInputField;

    public async void OnCreateLobbyButtonClicked()
    {
        await NetworkSessionOrchestrator.Instance.HostLanAsync();
    }

    public async void OnJoinLobbyButtonClicked()
    {
        await NetworkSessionOrchestrator.Instance.JoinLanAsync(ipInputField.text);
    }

    public void OnQuitButtonClicked()
    {
        Application.Quit();
    }
}