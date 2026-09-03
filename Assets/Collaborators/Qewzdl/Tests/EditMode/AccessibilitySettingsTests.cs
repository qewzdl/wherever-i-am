using NUnit.Framework;

// The part of the accessibility work that is a decision rather than wiring:
// what the settings file is allowed to contain when it comes back off disk, and
// what it lands on when it carries none of this yet.
public sealed class AccessibilitySettingsTests
{
    [Test]
    public void Sanitize_KeepsAccessibilityValuesInsideTheirControls()
    {
        GameSettingsData settings = GameSettingsData.CreateDefaults(1920, 1080, 0);

        settings.uiScale = 9f;
        settings.textSize = 99;
        settings.Sanitize(1);

        Assert.That(settings.uiScale, Is.EqualTo(GameSettingsData.MaxUiScale));
        Assert.That(settings.textSize, Is.EqualTo(GameSettingsData.TextSizeNames.Length - 1));

        settings.uiScale = -3f;
        settings.textSize = -7;
        settings.Sanitize(1);

        Assert.That(settings.uiScale, Is.EqualTo(GameSettingsData.MinUiScale));
        Assert.That(settings.textSize, Is.EqualTo(0));
    }

    // A settings file written before these existed has none of them in it, and
    // JsonUtility.FromJsonOverwrite leaves what it does not find alone. This is
    // the check that the defaults it lands on are the game as it behaved
    // yesterday rather than an interface that has silently changed size.
    [Test]
    public void Defaults_LeaveTheGameAsItWas()
    {
        GameSettingsData settings = GameSettingsData.CreateDefaults(1920, 1080, 0);

        Assert.That(settings.uiScale, Is.EqualTo(1f));
        Assert.That(settings.textSize, Is.EqualTo(1), "the middle step of the ladder");
        Assert.That(settings.reducedMotion, Is.False);
    }
}
