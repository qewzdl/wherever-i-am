using UnityEngine;
using UnityEngine.UI;

// The settings button, wherever it happens to be.
//
// There is one settings screen for the whole game, so this holds no reference
// to one: it asks the global scope at the moment of the click, which is also
// the only moment the answer matters. That is what lets the same button work
// in the main menu, in a lobby and over a running game without any of those
// scenes owning a screen or wiring one up.
[RequireComponent(typeof(Button))]
public sealed class SettingsScreenLauncher : MonoBehaviour
{
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

    // Public so a UnityEvent can reach it as well: a menu that opens settings
    // from something other than a button should not need another component.
    public void Open()
    {
        if (G.TryResolve(out ISettingsScreen settingsScreen))
        {
            settingsScreen.Open();
            return;
        }

        Debug.LogError(
            $"{nameof(SettingsScreenLauncher)} found no {nameof(ISettingsScreen)} to open.",
            this);
    }
}
