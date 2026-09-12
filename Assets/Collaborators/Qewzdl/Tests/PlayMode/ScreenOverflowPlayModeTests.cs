using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// Text that grows past the box it was given.
//
// Nothing in UI Toolkit clips by default, so a label that outgrows its row is
// not cut off - it is drawn over whatever is next to it. Two things make that
// happen and both are things players do: a translation that is longer than the
// English it replaced, and the text size setting, which goes up by about a
// third.
//
// A layout resolves without anything being rendered, so this can be measured
// rather than looked at: build every screen at the largest text, fill every
// label with the longest thing the tables know how to say, and ask whether
// anybody has ended up outside their parent.
public sealed class ScreenOverflowPlayModeTests
{
    private const string ScreensFolder = "Assets/Collaborators/Qewzdl/UI/Screens";

    private const string PanelPath =
        "Assets/Collaborators/Qewzdl/UI/UiPanelSettings.asset";

    private const string RussianPath =
        "Assets/Collaborators/Qewzdl/Configs/Localization/Locale_Russian.asset";

    // A pixel of slop. Layouts round, and a row that is a third of a pixel
    // proud of its parent is not what anybody means by overlapping.
    private const float Slack = 1f;

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
    public IEnumerator NoScreenSpillsOverItselfAtTheLargestText()
    {
        LocaleTable russian = AssetDatabase.LoadAssetAtPath<LocaleTable>(RussianPath);
        PanelSettings source = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);

        Assert.That(russian, Is.Not.Null, $"No table at '{RussianPath}'.");
        Assert.That(source, Is.Not.Null, $"No panel settings at '{PanelPath}'.");

        panel = Object.Instantiate(source);
        host = new GameObject(nameof(ScreenOverflowPlayModeTests));

        UIDocument document = host.AddComponent<UIDocument>();
        document.panelSettings = panel;

        List<string> spills = new();
        string[] guids = AssetDatabase.FindAssets("t:VisualTreeAsset", new[] { ScreensFolder });

        Assert.That(guids, Is.Not.Empty, "No screens were found.");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            VisualTreeAsset markup = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);

            if (markup == null)
                continue;

            document.visualTreeAsset = null;
            yield return null;

            document.visualTreeAsset = markup;

            VisualElement root = document.rootVisualElement;

            // The two things a player can do to a screen, both at once.
            root.AddToClassList("text--largest");
            Translate(root, russian);

            // Layout is two passes away: one to build, one to measure what the
            // new text did to it.
            yield return null;
            yield return null;

            Collect(root, System.IO.Path.GetFileName(path), spills);
        }

        Assert.That(
            spills,
            Is.Empty,
            "These are drawn outside the element that holds them, which means " +
            "on top of whatever is next to it:\n  " + string.Join("\n  ", spills));
    }

    // The Russian for everything the markup says, because it is the longest
    // the game currently gets - and a screen that survives it survives the
    // English it was drawn for.
    private static void Translate(VisualElement root, LocaleTable table)
    {
        root.Query<Label>().ForEach(label => Retext(label, table));
        root.Query<Button>().ForEach(button => Retext(button, table));
    }

    private static void Retext(TextElement element, LocaleTable table)
    {
        if (string.IsNullOrEmpty(element.text))
            return;

        table.TryTranslate(element.text, out string translation);
        element.text = translation;
    }

    // Measured rather than compared.
    //
    // The first version of this asked whether an element had ended up bigger
    // than its parent, and found nothing - because text that overflows does
    // not make its element bigger. A label with a fixed width and no wrapping
    // keeps its box and paints the rest of the sentence outside it, over
    // whatever the next column holds. So the question is not how big the box
    // is, it is how big the words are.
    private static void Collect(VisualElement element, string screen, List<string> spills)
    {
        if (element is TextElement text && !string.IsNullOrEmpty(text.text))
            Check(text, screen, spills);

        foreach (VisualElement child in element.Children())
            Collect(child, screen, spills);
    }

    private static void Check(TextElement element, string screen, List<string> spills)
    {
        Rect box = element.contentRect;

        if (box.width <= 0f || float.IsNaN(box.width))
            return;

        bool wraps = element.resolvedStyle.whiteSpace == WhiteSpace.Normal;

        if (wraps)
        {
            // Wrapping trades width for height, so what can overflow is the
            // height - and only where something else decided that height.
            float needed = element.MeasureTextSize(
                element.text,
                box.width,
                VisualElement.MeasureMode.AtMost,
                0f,
                VisualElement.MeasureMode.Undefined).y;

            if (needed > box.height + Slack && box.height > 0f)
            {
                spills.Add(
                    $"{screen} · {Describe(element)} needs {needed:0}px of " +
                    $"height and has {box.height:0}px - \"{Shorten(element.text)}\"");
            }

            return;
        }

        float wide = element.MeasureTextSize(
            element.text,
            0f,
            VisualElement.MeasureMode.Undefined,
            0f,
            VisualElement.MeasureMode.Undefined).x;

        if (wide > box.width + Slack)
        {
            spills.Add(
                $"{screen} · {Describe(element)} needs {wide:0}px of width " +
                $"and has {box.width:0}px - \"{Shorten(element.text)}\"");
        }
    }

    private static string Shorten(string text)
    {
        return text.Length <= 40 ? text : text.Substring(0, 37) + "...";
    }

    private static string Describe(VisualElement element)
    {
        if (!string.IsNullOrEmpty(element.name))
            return $"#{element.name}";

        foreach (string className in element.GetClasses())
            return $".{className}";

        return element.GetType().Name;
    }
}
