using TMPro;
using UnityEngine;

public class LanConnectionUi : MonoBehaviour
{
    [SerializeField] private TMP_InputField ipInputField;

    public async void OnHostButtonClicked()
    {
        await NetworkSessionOrchestrator.Instance.HostLanAsync();
    }

    public async void OnClientButtonClicked()
    {
        await NetworkSessionOrchestrator.Instance.JoinLanAsync(ipInputField.text);
    }

    public void OnDisconnectButtonClicked()
    {
        NetworkSessionOrchestrator.Instance.ShutdownToMainMenu();
    }
}