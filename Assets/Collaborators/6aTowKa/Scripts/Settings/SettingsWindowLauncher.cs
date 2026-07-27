using UnityEngine;
using UnityEngine.UI;

/// <summary>Прикрепляется к сценовой кнопке «Настройки» и открывает окно той же сцены.</summary>
[RequireComponent(typeof(Button))]
public sealed class SettingsWindowLauncher : MonoBehaviour
{
    [SerializeField] private SettingsWindow settingsWindow;
    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(Open);
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(Open);
    }

    public void Open()
    {
        settingsWindow?.Open();
    }
}
