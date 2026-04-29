using UnityEngine;
using TMPro;

public class LANConnectionUI : MonoBehaviour
{
    [SerializeField] private TMP_InputField ipInputField;

    public async void OnHostButtonClicked()
    {
        ConnectionResult result = await NetworkConnectionService.Instance.StartHostAsync();
    }

    public async void OnClientButtonClicked()
    {
        ConnectionResult result = await NetworkConnectionService.Instance.StartClientAsync(ipInputField.text);
    }

    public void OnDisconnectButtonClicked()
    {
        NetworkConnectionService.Instance.Shutdown();
    }
}