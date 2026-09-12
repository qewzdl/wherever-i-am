using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// What can be told the truth about without a screen.
//
// A batchmode run has no rendering, so it never advances a transition - the
// opacity arrives at its end value in the same frame the class lands, however
// long the stylesheet says. That makes "does the fade take three seconds"
// untestable here, and it is why the curtain went unnoticed as instant for
// several rounds.
//
// The duration itself is not an animation, though. It is a resolved style, and
// a style resolves whether or not anybody is looking. So this asks the one
// question that was actually in doubt: when the moment comes to lift the
// curtain, is there a duration on it, or is it still nought from being put
// down?
public sealed class SceneCurtainStylePlayModeTests
{
    private const string PanelPath =
        "Assets/Collaborators/Qewzdl/UI/UiPanelSettings.asset";

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

    // The fade is written frame by frame in code now, so what the stylesheet
    // still owes the curtain is what it looks like: black, and over the whole
    // screen. An element built in code gets that from the panel's theme rather
    // than from a Style tag in a markup file, and this is how we know it
    // arrived.
    [UnityTest]
    public IEnumerator TheCurtainRuleReachesAnElementBuiltInCode()
    {
        PanelSettings source = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
        Assert.That(source, Is.Not.Null, $"No panel settings at '{PanelPath}'.");

        panel = Object.Instantiate(source);
        host = new GameObject(nameof(SceneCurtainStylePlayModeTests));

        UIDocument document = host.AddComponent<UIDocument>();
        document.panelSettings = panel;

        VisualElement curtain = new();
        curtain.AddToClassList("curtain");
        document.rootVisualElement.Add(curtain);

        yield return null;
        yield return null;

        Assert.That(
            curtain.resolvedStyle.backgroundColor.a,
            Is.GreaterThan(0.9f),
            "The curtain is not opaque, so the theme did not reach it.");

        Assert.That(
            curtain.resolvedStyle.width,
            Is.GreaterThan(1f),
            $"The curtain is {curtain.resolvedStyle.width} wide, so the rule " +
            "that makes it cover the screen did not reach it.");
    }
}
