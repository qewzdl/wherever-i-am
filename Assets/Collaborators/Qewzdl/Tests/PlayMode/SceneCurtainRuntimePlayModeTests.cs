using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// The last link. The styling is measured elsewhere and the deciding is
// measured elsewhere; what is left is whether the panel SceneCurtain builds
// for itself is a real one in a real game - attached, sized, and holding the
// element it was made for.
//
// An element that is not on a panel draws nothing and says nothing about it.
public sealed class SceneCurtainRuntimePlayModeTests
{
    private const string BootstrapScenePath =
        "Assets/Collaborators/Qewzdl/Scenes/Bootstrap.unity";

    [UnityTest]
    public IEnumerator TheCurtainIsOnALivePanelOnceTheGameIsUp()
    {
        bool previous = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true;

        try
        {
            yield return SceneManager.LoadSceneAsync(
                BootstrapScenePath,
                LoadSceneMode.Single);

            // Waited for rather than counted. The film in front of the menu
            // never prepares in a batchmode run, so the startup scene arrives
            // whenever its timeout says - which is seconds, not frames.
            float until = Time.realtimeSinceStartup + 20f;

            while (Time.realtimeSinceStartup < until &&
                   GameObject.Find(nameof(SceneCurtain)) == null)
            {
                yield return null;
            }

            GameObject host = GameObject.Find(nameof(SceneCurtain));

            Assert.That(
                host,
                Is.Not.Null,
                "SceneCurtain never built its panel, so nothing was ever " +
                "covered - Initialize did not run, or it ran and returned.");

            UIDocument document = host.GetComponent<UIDocument>();

            Assert.That(document, Is.Not.Null, "The host has no document.");
            Assert.That(
                document.panelSettings,
                Is.Not.Null,
                "The document has no panel settings, so it has no panel.");

            VisualElement root = document.rootVisualElement;
            Assert.That(root, Is.Not.Null, "The document has no root.");

            // The menu is a curtained scene, so by now one has been dropped
            // and is on its way up. Classes rather than opacity: a transition
            // does not advance in a batchmode run, so the only honest question
            // is whether the element was made and told to lift.
            until = Time.realtimeSinceStartup + 20f;

            while (Time.realtimeSinceStartup < until &&
                   root.Q(className: "curtain") == null)
            {
                yield return null;
            }

            VisualElement curtain = root.Q(className: "curtain");

            Assert.That(
                curtain,
                Is.Not.Null,
                $"No curtain under the root ({root.childCount} children), so " +
                "no curtained scene was ever recognised as one.");

            Assert.That(
                curtain.panel,
                Is.Not.Null,
                "The curtain is not attached to a panel, so it is never drawn " +
                "however black it is.");

            // A style resolves at layout, and this element was made in the
            // frame the scene loaded, so there has not been one yet.
            yield return null;
            yield return null;

            Assert.That(
                curtain.resolvedStyle.width,
                Is.GreaterThan(1f),
                $"The curtain is {curtain.resolvedStyle.width} wide, so the " +
                ".curtain rule did not reach it inside the running game.");

            Assert.That(
                curtain.resolvedStyle.backgroundColor.a,
                Is.GreaterThan(0.9f),
                "The curtain has no opaque colour to come off.");

            // And the fade itself, which is a number written every frame
            // rather than a transition, so it can finally be measured: a
            // batchmode run does not render, and a USS transition never
            // advances without rendering.
            SceneManager.LoadScene(
                "Assets/Collaborators/Qewzdl/Scenes/Lobby.unity",
                LoadSceneMode.Single);

            yield return null;

            VisualElement dropped = root.Q(className: "curtain");

            Assert.That(
                dropped,
                Is.Not.Null,
                "Loading a curtained scene left no curtain at all.");

            Assert.That(
                dropped,
                Is.Not.SameAs(curtain),
                "The lobby loaded and the curtain was not dropped again.");

            float started = Time.realtimeSinceStartup;
            float highest = 0f;
            float lowest = 1f;
            bool between = false;

            while (Time.realtimeSinceStartup - started < 6f &&
                   dropped.panel != null)
            {
                float opacity = dropped.resolvedStyle.opacity;
                highest = Mathf.Max(highest, opacity);
                lowest = Mathf.Min(lowest, opacity);

                if (opacity > 0.1f && opacity < 0.9f)
                    between = true;

                yield return null;
            }

            float took = Time.realtimeSinceStartup - started;

            Assert.That(
                highest,
                Is.GreaterThan(0.9f),
                $"The curtain was never black - the most it reached was {highest}.");

            Assert.That(
                between,
                Is.True,
                "The curtain went from black to gone without ever being " +
                "halfway, so it was a cut rather than a fade.");

            Assert.That(
                took,
                Is.GreaterThan(1f),
                $"The whole thing was over in {took:0.00}s, and the config " +
                "asks for three.");
        }
        finally
        {
            LogAssert.ignoreFailingMessages = previous;
        }
    }
}
