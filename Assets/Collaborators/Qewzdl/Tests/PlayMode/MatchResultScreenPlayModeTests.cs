using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// The result screen is read once per match and never while anybody is testing
// anything, so a renamed element in the markup would go unnoticed until a
// match ended in front of a player and said nothing.
//
// This builds the tree the way the game does and asks for the three things the
// code looks up by name, then checks the one rule that matters: the outcome is
// the largest thing on the screen, and the loss is the colour that is only
// used for a loss.
public sealed class MatchResultScreenPlayModeTests
{
    private const string MarkupPath =
        "Assets/Collaborators/Qewzdl/UI/Screens/MatchResult.uxml";

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

    [UnityTest]
    public IEnumerator TheScreenHasEverythingTheCodeAsksItFor()
    {
        VisualTreeAsset markup =
            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MarkupPath);

        PanelSettings source =
            AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);

        Assert.That(markup, Is.Not.Null, $"No markup at '{MarkupPath}'.");
        Assert.That(source, Is.Not.Null, $"No panel settings at '{PanelPath}'.");

        panel = Object.Instantiate(source);
        host = new GameObject(nameof(MatchResultScreenPlayModeTests));

        UIDocument document = host.AddComponent<UIDocument>();
        document.panelSettings = panel;
        document.visualTreeAsset = markup;

        yield return null;
        yield return null;

        VisualElement root = document.rootVisualElement;

        // The two the feature looks up. A rename here is a match that ends
        // in silence.
        VisualElement screen = root.Q<VisualElement>("Screen");
        Label outcome = root.Q<Label>("Outcome");

        Assert.That(screen, Is.Not.Null, "No 'Screen'.");
        Assert.That(outcome, Is.Not.Null, "No 'Outcome'.");

        // And no panel, which is the shape of this screen rather than an
        // omission: the frame goes dark and the sentence is written on it.
        Assert.That(
            root.Q<VisualElement>(className: "panel"),
            Is.Null,
            "The result screen has grown a panel - it is meant to be the " +
            "whole frame going dark.");

        Assert.That(
            screen.resolvedStyle.backgroundColor.a,
            Is.GreaterThan(0.5f),
            "The screen layer is not dark, so the sentence is written over a " +
            "match nobody is playing any more.");

        Assert.That(
            root.Q<Label>("Note"),
            Is.Not.Null,
            "No 'Note' - the line saying what happens after the match.");

        outcome.text = "Everyone was caught";
        yield return null;

        float ordinary = outcome.resolvedStyle.fontSize;
        Color quiet = outcome.resolvedStyle.color;

        Assert.That(
            ordinary,
            Is.GreaterThan(34f),
            $"The outcome is {ordinary}px, which is no larger than a title - " +
            "the result screen has one sentence and that is the point of it.");

        // The loss, and only the loss, breaks the palette's quiet.
        screen.AddToClassList("result--defeat");
        yield return null;

        Assert.That(
            outcome.resolvedStyle.color,
            Is.Not.EqualTo(quiet),
            "A loss looks exactly like a win, so the colour that exists for " +
            "one of them is reaching neither.");
    }
}
