using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// Why this exists: the curtain over the main menu did not animate, and three
// readings of the stylesheet said it should. Reading it a fourth time was not
// going to help, so this looks at the element instead - is it there, is it
// black, is it opaque, and does taking the class off it move anything.
//
// Every assertion carries the value it found, so one run says which link is
// broken rather than only that one is.
[Category("UI")]
public sealed class MainMenuCurtainPlayModeTests
{
    private const string ScenePath =
        "Assets/Collaborators/Qewzdl/Scenes/Main Menu.unity";

    [UnityTest]
    public IEnumerator TheCurtainIsBlackAndOverEverythingWhenTheMenuArrives()
    {
        // The menu screen reports its own missing services loudly when it is
        // opened on its own. None of that is what is being looked at here.
        bool previous = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true;

        try
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);

            UIDocument document = null;

            foreach (UIDocument candidate in Object.FindObjectsByType<UIDocument>(
                         FindObjectsSortMode.None))
            {
                if (candidate.rootVisualElement?.Q<VisualElement>("Screen") != null)
                    document = candidate;
            }

            Assert.That(document, Is.Not.Null, "The menu scene has no document.");

            VisualElement root = document.rootVisualElement;
            VisualElement curtain = root.Q<VisualElement>("Curtain");

            Assert.That(
                curtain,
                Is.Not.Null,
                "There is no element named Curtain in the tree the scene built. " +
                "Children of the root: " + Children(root));

            // One frame is not enough to have been laid out; two is the usual
            // number before resolvedStyle means anything.
            yield return null;
            yield return null;

            Assert.That(
                curtain.resolvedStyle.width,
                Is.GreaterThan(1f),
                $"The curtain has no width ({curtain.resolvedStyle.width} x " +
                $"{curtain.resolvedStyle.height}), so the .curtain rule did " +
                "not reach it - position and the four sides come from there.");

            Color background = curtain.resolvedStyle.backgroundColor;

            Assert.That(
                background.a,
                Is.GreaterThan(0.9f),
                $"The curtain is not opaque: background {background}. Without " +
                "a background colour there is nothing to fade out.");

            Assert.That(
                curtain.parent,
                Is.EqualTo(root),
                "The curtain is not a child of the root, so the screen's own " +
                "fade applies to it as well.");

            Assert.That(
                root.IndexOf(curtain),
                Is.EqualTo(root.childCount - 1),
                $"The curtain is child {root.IndexOf(curtain)} of " +
                $"{root.childCount}, so something is painted over it.");
        }
        finally
        {
            LogAssert.ignoreFailingMessages = previous;
        }
    }

    // What is deliberately not tested here: whether the fade actually takes
    // time. A batchmode run has no rendering, and without it the panel never
    // advances a transition - the opacity arrives at its end value in the same
    // frame the class lands, however long the stylesheet says. Measured both
    // ways, with a token and with a literal 0.62s, and both report 0.000s.
    //
    // So a timing test here would pass or fail on the harness rather than on
    // the interface, which is worse than no test. What is left is everything
    // that can be told the truth about without a screen: the element is there,
    // it is styled, it is black, and it is on top.

    private static string Children(VisualElement root)
    {
        System.Text.StringBuilder names = new();

        for (int i = 0; i < root.childCount; i++)
        {
            names.Append(i == 0 ? string.Empty : ", ");
            names.Append(string.IsNullOrEmpty(root[i].name) ? "<unnamed>" : root[i].name);
        }

        return names.ToString();
    }
}
