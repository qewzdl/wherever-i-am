using NUnit.Framework;
using UnityEditor;

// The curtain is decided before it is drawn: a scene is loaded, its name is
// turned into a kind, and the kind is looked up in the config. Three moving
// parts, none of which say anything out loud when they disagree - a name that
// resolves to Unknown simply means no scene is ever covered, silently.
//
// The drawing has its own test. This is the deciding.
public sealed class SceneCurtainDecisionTests
{
    private const string ConfigPath =
        "Assets/Collaborators/Qewzdl/Configs/Scenes/SceneCurtainConfig.asset";

    private const string SettingsPath =
        "Assets/Collaborators/Qewzdl/Settings/ProjectSettings.asset";

    [TestCase(ProjectSceneKind.MainMenu)]
    [TestCase(ProjectSceneKind.Lobby)]
    [TestCase(ProjectSceneKind.Game)]
    public void TheNameASceneLoadsUnderIsTheNameTheCurtainLooksUp(ProjectSceneKind kind)
    {
        ProjectSettings settings =
            AssetDatabase.LoadAssetAtPath<ProjectSettings>(SettingsPath);

        SceneCurtainConfig config =
            AssetDatabase.LoadAssetAtPath<SceneCurtainConfig>(ConfigPath);

        Assert.That(settings, Is.Not.Null, $"No project settings at '{SettingsPath}'.");
        Assert.That(config, Is.Not.Null, $"No curtain config at '{ConfigPath}'.");

        string sceneName = settings.GetSceneName(kind);

        Assert.That(
            sceneName,
            Is.Not.Null.And.Not.Empty,
            $"{kind} has no scene name, so nothing can ever match it.");

        // What SceneCurtain is handed by sceneLoaded is the name alone - a
        // Scene struct carries no path worth trusting at that moment - so the
        // one-argument lookup is the one that has to work.
        ProjectSceneKind resolved = settings.GetSceneKind(sceneName, string.Empty);

        Assert.That(
            resolved,
            Is.EqualTo(kind),
            $"'{sceneName}' resolves to {resolved}, so a scene loading under " +
            "that name is never recognised as the one it is.");

        Assert.That(
            config.Covers(resolved),
            Is.True,
            $"{kind} is not covered, so it opens without the lightening.");
    }
}
