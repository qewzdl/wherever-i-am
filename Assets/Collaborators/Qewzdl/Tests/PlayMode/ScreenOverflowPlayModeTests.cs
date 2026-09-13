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

    // The rows nobody can see by opening a markup file.
    //
    // A lobby's roster and a server browser are built a row at a time in code,
    // so the screen check above walks straight past them - and they are the
    // ones with a height nailed on: forty-six pixels, whatever the text does.
    // Nothing clips, so a name that outgrows that row is drawn across the row
    // below it.
    //
    // Built here the way the two documents build them, by the classes they
    // hang on. That is a copy of a structure and it can drift; it is also the
    // only way to ask this question without a running lobby, and the classes
    // are the part that the stylesheet actually answers.
    [UnityTest]
    public IEnumerator NoRowBuiltInCodeSpillsAtTheLargestText()
    {
        PanelSettings source = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
        Assert.That(source, Is.Not.Null, $"No panel settings at '{PanelPath}'.");

        panel = Object.Instantiate(source);
        host = new GameObject(nameof(ScreenOverflowPlayModeTests));

        UIDocument document = host.AddComponent<UIDocument>();
        document.panelSettings = panel;

        VisualElement root = document.rootVisualElement;
        root.AddToClassList("text--largest");

        // A name as long as the field lets somebody type, because that is the
        // longest a row will ever have to hold.
        // Sixteen of the widest letter there is. The name field stops at
        // sixteen characters, so this is the longest a row will ever be asked
        // to hold - not a guess at a long name, the actual ceiling.
        const string LongName = "WWWWWWWWWWWWWWWW";

        // In the columns they actually live in. Dropped straight onto the
        // root they get the whole window to spread into, which is a width no
        // row in this game has ever had - the first version of this passed
        // with a name half a sentence long because of it.
        root.Add(Column("panel--rail", RosterRow(LongName)));
        root.Add(Column("panel--wide", BrowserRow(LongName)));

        yield return null;
        yield return null;

        List<string> spills = new();
        Collect(root, "rows built in code", spills);

        Assert.That(
            spills,
            Is.Empty,
            "These are drawn outside the row that holds them, which means on " +
            "top of the row below:\n  " + string.Join("\n  ", spills));
    }

    private static VisualElement Column(string modifier, VisualElement row)
    {
        VisualElement column = new();
        column.AddToClassList("panel");
        column.AddToClassList(modifier);
        column.Add(row);
        return column;
    }

    private static VisualElement RosterRow(string playerName)
    {
        VisualElement row = new();
        row.AddToClassList("roster__row");

        Label name = new(playerName);
        name.AddToClassList("roster__name");
        row.Add(name);

        VisualElement badges = new();
        badges.AddToClassList("roster__badges");
        row.Add(badges);

        Label role = new("Owner");
        role.AddToClassList("roster__role");
        badges.Add(role);

        Label status = new("Not ready");
        status.AddToClassList("roster__status");
        badges.Add(status);

        Button kick = new() { text = "Remove" };
        kick.AddToClassList("roster__kick");
        row.Add(kick);

        return row;
    }

    private static VisualElement BrowserRow(string lobbyName)
    {
        Button row = new() { text = string.Empty };
        row.AddToClassList("button");
        row.AddToClassList("browser__row");

        Label name = new(lobbyName);
        name.AddToClassList("browser__name");
        row.Add(name);

        Label count = new("4/8");
        count.AddToClassList("browser__count");
        row.Add(count);

        return row;
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
