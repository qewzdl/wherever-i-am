using NUnit.Framework;
using UnityEditor;

// The curtain used to be an element in the main menu's markup, and a test
// looked at it there. It is a service now, so what is worth guarding is not
// the element - the code that makes it is three lines and cannot lose its own
// class - but the list of scenes, which is a decision somebody made and which
// nothing else in the project would notice going missing.
public sealed class SceneCurtainTests
{
    private const string ConfigPath =
        "Assets/Collaborators/Qewzdl/Configs/Scenes/SceneCurtainConfig.asset";

    [Test]
    public void TheScenesThatOpenOutOfTheDarkAreTheOnesThatWereAskedFor()
    {
        SceneCurtainConfig config =
            AssetDatabase.LoadAssetAtPath<SceneCurtainConfig>(ConfigPath);

        Assert.That(config, Is.Not.Null, $"No curtain config at '{ConfigPath}'.");

        Assert.That(config.Covers(ProjectSceneKind.MainMenu), Is.True);
        Assert.That(config.Covers(ProjectSceneKind.Lobby), Is.True);
        Assert.That(config.Covers(ProjectSceneKind.Game), Is.True);

        // The bootstrap scene is the one the film plays over. Covering it
        // would mean lightening into a black screen and then playing a film
        // on top of that.
        Assert.That(config.Covers(ProjectSceneKind.Bootstrap), Is.False);
        Assert.That(config.Covers(ProjectSceneKind.Unknown), Is.False);
    }

    // A scene listed for no time at all is the fault this whole thing spent a
    // day being: black for one frame, which reads as a flicker rather than as
    // anything deliberate. If somebody wants a scene to simply appear, the way
    // to say so is to take it off the list.
    [Test]
    public void NoSceneIsCoveredForNoTime()
    {
        SceneCurtainConfig config =
            AssetDatabase.LoadAssetAtPath<SceneCurtainConfig>(ConfigPath);

        Assert.That(config, Is.Not.Null, $"No curtain config at '{ConfigPath}'.");

        foreach (SceneCurtainConfig.Entry entry in config.ScenesForEditor)
        {
            Assert.That(
                entry.Seconds,
                Is.GreaterThan(0.2f),
                $"{entry.Scene} is covered for {entry.Seconds}s, which nobody " +
                "will read as a lightening. Remove it from the list instead.");
        }
    }
}
