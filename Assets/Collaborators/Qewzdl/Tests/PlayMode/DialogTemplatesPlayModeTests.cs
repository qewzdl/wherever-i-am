using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// The two dialogs live in UI/Templates and are placed on three screens. The
// code finds them by the panel's name and then by the names inside the
// template, and a screen says what its way out is called by overriding the
// template's text - so a renamed element or an override that stopped applying
// would leave a dialog that opens with nothing in it, or a button that says
// the wrong thing, and nothing else would notice until it was on screen.
public sealed class DialogTemplatesPlayModeTests
{
    private const string Screens = "Assets/Collaborators/Qewzdl/UI/Screens/";
    private const string PanelPath = "Assets/Collaborators/Qewzdl/UI/UiPanelSettings.asset";

    private GameObject host;
    private PanelSettings panel;

    [TearDown]
    public void TearDown()
    {
        if (host != null)
            Object.DestroyImmediate(host);

        if (panel != null)
            Object.DestroyImmediate(panel);
    }

    [UnityTest]
    public IEnumerator EveryPlacedDialogHasWhatItsScreenAsksFor()
    {
        yield return Check("MainMenu.uxml", "BusyPanel", "Cancel", "Step", "Text", "Detail", "Elapsed");
        yield return Check("Lobby.uxml", "MatchTransitionPanel", "Leave session", "Step", "Text", "Detail", "Elapsed");
        yield return Check("Lobby.uxml", "ConfirmPanel", "Cancel", "Text");
        yield return Check("Settings.uxml", "ConfirmPanel", "Revert", "Text");
    }

    private IEnumerator Check(string screen, string dialogName, string wayOut, params string[] labels)
    {
        TearDown();

        panel = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath));
        host = new GameObject(nameof(DialogTemplatesPlayModeTests));

        UIDocument document = host.AddComponent<UIDocument>();
        document.panelSettings = panel;
        document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Screens + screen);
        yield return null;

        VisualElement dialog = document.rootVisualElement.Q<VisualElement>(dialogName);
        string where = $"{screen} {dialogName}";

        Assert.That(dialog, Is.Not.Null, $"{where} is missing.");
        Assert.That(dialog.ClassListContains("overlay"), Is.True, $"{where} is not an overlay.");

        foreach (string label in labels)
            Assert.That(dialog.Q<Label>(label), Is.Not.Null, $"{where} has no '{label}'.");

        Button cancel = dialog.Q<Button>("CancelButton");
        Assert.That(cancel, Is.Not.Null, $"{where} has no way out.");
        Assert.That(cancel.text, Is.EqualTo(wayOut), $"{where} names its way out wrongly.");
    }
}
