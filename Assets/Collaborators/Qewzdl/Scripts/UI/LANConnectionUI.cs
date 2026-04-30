using UnityEngine;
using TMPro;

public class LANConnectionUI : MonoBehaviour
{
    [SerializeField] private TMP_InputField ipInputField;

    public async void OnHostButtonClicked()
    {
        ConnectionResult result = await NetworkConnectionService.Instance.StartHostAsync();

        if (!result.Success) AudioManager.Instance.UI.PlayError();
    }

    public async void OnClientButtonClicked()
    {
        ConnectionResult result = await NetworkConnectionService.Instance.StartClientAsync(ipInputField.text);

        if (!result.Success) AudioManager.Instance.UI.PlayError();
    }

    public void OnDisconnectButtonClicked()
    {
        NetworkConnectionService.Instance.Shutdown();
    }
}