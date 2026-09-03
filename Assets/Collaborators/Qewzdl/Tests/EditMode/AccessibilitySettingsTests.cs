using NUnit.Framework;

// The two pieces of the accessibility work that are decisions rather than
// wiring: what a crouch key press means in each mode, and what the settings
// file is allowed to contain when it comes back off disk.
public sealed class AccessibilitySettingsTests
{
    [Test]
    public void NextCrouchState_Toggle_FlipsOnPressAndIgnoresRelease()
    {
        Assert.That(
            PlayerController.NextCrouchState(false, true, false, out bool crouched),
            Is.True,
            "a press while standing should crouch");
        Assert.That(crouched, Is.True);

        Assert.That(
            PlayerController.NextCrouchState(false, true, true, out bool stood),
            Is.True,
            "a press while crouched should stand");
        Assert.That(stood, Is.False);

        // A toggle hears one edge. Acting on the release as well is how the key
        // ends up doing nothing at all.
        Assert.That(
            PlayerController.NextCrouchState(false, false, true, out _),
            Is.False,
            "a release should say nothing in toggle mode");
        Assert.That(
            PlayerController.NextCrouchState(false, false, false, out _),
            Is.False);
    }

    [Test]
    public void NextCrouchState_Hold_FollowsTheKeyAndOnlyReportsChanges()
    {
        Assert.That(
            PlayerController.NextCrouchState(true, true, false, out bool crouched),
            Is.True);
        Assert.That(crouched, Is.True);

        Assert.That(
            PlayerController.NextCrouchState(true, false, true, out bool stood),
            Is.True);
        Assert.That(stood, Is.False);

        // The input system repeats a phase more than once often enough that a
        // stance change per callback would be a stance change per frame.
        Assert.That(
            PlayerController.NextCrouchState(true, true, true, out _),
            Is.False,
            "holding a key already held is not a change");
        Assert.That(
            PlayerController.NextCrouchState(true, false, false, out _),
            Is.False);
    }

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
        Assert.That(settings.crouchIsHold, Is.False, "crouch was a toggle before it was a setting");
    }
}
