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

        // The page turns before the next map is written on it.
        yield return new WaitForSecondsRealtime(0.6f);
        string secondMap = mapName.text;

        VisualElement sheet = root.Q<VisualElement>("MapSheet");
        Assert.That(secondMap, Is.EqualTo(catalog.GetMapAt(1).DisplayName), "The page never turned to the second map.");
        Assert.That(sheet.GetClasses(), Has.None.Contains("--leaving"), "The page was left mid-turn.");
        Assert.That(sheet.GetClasses(), Has.None.Contains("--arriving"), "The page was left mid-turn.");

        VisualElement dots = root.Q<VisualElement>("MapDots");
        Assert.That(dots.childCount, Is.EqualTo(catalog.Count), "Not one dot a map.");
        Assert.That(dots[1].ClassListContains("map-screen__dot--current"), Is.True, "The dots do not follow the page.");

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

    // Create and Join is the step before the maps, so leaving the maps for a
    // lobby lands back on it - not on the menu underneath.
    [UnityTest]
    public IEnumerator BackFromTheMapsReturnsToCreateOrJoin()
    {
        GameMapCatalog catalog = AssetDatabase.LoadAssetAtPath<GameMapCatalog>(CatalogPath);

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
        VisualElement multiplayer = root.Q<VisualElement>("MultiplayerScreen");
        VisualElement maps = root.Q<VisualElement>("MapScreen");

        PlayModeTestReflection.Invoke(menu, "OpenMultiplayer");
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.That(multiplayer.ClassListContains("multiplayer-screen--open"), Is.True, "Create and Join did not open.");

        // Opening hands Create the focus. The lift belongs to the pointer: a
        // choice raised by focus stayed up after a click, and hovering it
        // again showed nothing.
        Button create = root.Q<Button>("HostButton");
        Assert.That(create.focusController.focusedElement, Is.EqualTo(create), "Create was not focused.");
        Assert.That(create.resolvedStyle.scale.value.x, Is.EqualTo(1f), "Focus raised Create as if hovered.");

        // Nothing was pressed yet, so this focus is the keyboard's to show;
        // after a click it is not.
        Assert.That(create.ClassListContains("multiplayer-screen__choice--focused"), Is.True, "Keyboard focus is not shown.");

        PlayModeTestReflection.SetField(menu, "pointerDriven", true);
        create.Blur();
        create.Focus();
        yield return null;
        Assert.That(create.ClassListContains("multiplayer-screen__choice--focused"), Is.False, "A click left Create lit.");

        PlayModeTestReflection.Invoke(menu, "ChooseMapToHost");
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.That(maps.ClassListContains("map-screen--open"), Is.True, "The maps did not open.");
        Assert.That(multiplayer.ClassListContains("multiplayer-screen--open"), Is.False, "Create and Join stayed under the maps.");

        PlayModeTestReflection.Invoke(menu, "CloseMapSelect");
        yield return new WaitForSecondsRealtime(0.3f);

        Assert.That(maps.ClassListContains("map-screen--open"), Is.False, "The maps stayed open.");
        Assert.That(multiplayer.ClassListContains("multiplayer-screen--open"), Is.True, "Back skipped Create and Join.");
    }
}
