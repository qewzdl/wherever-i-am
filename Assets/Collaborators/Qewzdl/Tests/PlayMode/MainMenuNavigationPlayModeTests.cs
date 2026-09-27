using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// Back goes back one screen, not to wherever the menu began. The list of rooms
// is reached from the map screen, so that is where leaving it lands - on the
// same map, not on its first page and not past it to the menu.
public sealed class MainMenuNavigationPlayModeTests
{
    private const string MarkupPath = "Assets/Collaborators/Qewzdl/UI/Screens/MainMenu.uxml";
    private const string PanelPath = "Assets/Collaborators/Qewzdl/UI/UiPanelSettings.asset";
    private const string CatalogPath = "Assets/Collaborators/Qewzdl/Configs/Maps/GameMapCatalog.asset";

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
    public IEnumerator BackFromTheRoomsReturnsToTheMapTheyWereListedFor()
    {
        GameMapCatalog catalog = AssetDatabase.LoadAssetAtPath<GameMapCatalog>(CatalogPath);
        Assume.That(catalog != null && catalog.Count > 1, "Needs two maps to tell the pages apart.");

        panel = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath));
        host = new GameObject(nameof(MainMenuNavigationPlayModeTests));
        host.SetActive(false);

        UIDocument document = host.AddComponent<UIDocument>();
        document.panelSettings = panel;
        document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MarkupPath);
        MainMenuDocument menu = host.AddComponent<MainMenuDocument>();
        host.SetActive(true);

        menu.Construct(new LobbySessionServiceProbe(), null, null, null, catalog);
        yield return null;

        VisualElement root = document.rootVisualElement;
        VisualElement mapScreen = root.Q<VisualElement>("MapScreen");
        VisualElement joinScreen = root.Q<VisualElement>("JoinScreen");
        Label mapName = root.Q<Label>("MapName");

        // Screens open a tick after they are asked to, so each step waits
        // for the last to land.
        PlayModeTestReflection.Invoke(menu, "ChooseMapToJoin");
        yield return new WaitForSecondsRealtime(0.3f);
        PlayModeTestReflection.Invoke(menu, "ShowNextMap");
        string secondMap = mapName.text;

        PlayModeTestReflection.Invoke(menu, "ConfirmMap");
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.That(joinScreen.ClassListContains("join-screen--open"), Is.True, "The rooms did not open.");

        // What the Back button and Escape both call. A batch run has no
        // focused window, and Escape goes to whatever has focus, so it is
        // called rather than pressed.
        PlayModeTestReflection.Invoke(menu, "ReturnToMapSelect");

        yield return new WaitForSecondsRealtime(0.3f);

        Assert.That(joinScreen.ClassListContains("join-screen--open"), Is.False, "The rooms stayed open.");
        Assert.That(mapScreen.ClassListContains("map-screen--open"), Is.True, "Back skipped the map screen.");
        Assert.That(mapName.text, Is.EqualTo(secondMap), "Back returned to a different map.");
    }
}
