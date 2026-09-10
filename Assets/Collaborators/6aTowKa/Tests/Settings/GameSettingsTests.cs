using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class GameSettingsTests
{
    [Test]
    public void Sanitize_ClampsUnsafeValues()
    {
        GameSettingsData settings = GameSettingsData.CreateDefaults(1920, 1080, 2);
        settings.resolutionWidth = 10;
        settings.resolutionHeight = 20;
        settings.fullScreenMode = 999;
        settings.qualityLevel = 99;
        settings.frameRateLimit = 5;
        settings.masterVolume = 2f;
        settings.musicVolume = -1f;
        settings.mouseSensitivity = 1000f;
        settings.fieldOfView = 5f;
        settings.cameraSmoothingIntensity = 4f;

        settings.Sanitize(4);

        Assert.That(settings.resolutionWidth, Is.EqualTo(640));
        Assert.That(settings.resolutionHeight, Is.EqualTo(360));
        Assert.That(settings.fullScreenMode, Is.EqualTo((int)FullScreenMode.FullScreenWindow));
        Assert.That(settings.qualityLevel, Is.EqualTo(3));
        Assert.That(settings.frameRateLimit, Is.EqualTo(30));
        Assert.That(settings.masterVolume, Is.EqualTo(1f));
        Assert.That(settings.musicVolume, Is.EqualTo(0f));
        Assert.That(settings.mouseSensitivity, Is.EqualTo(GameSettingsData.MaxMouseSensitivity));
        Assert.That(settings.fieldOfView, Is.EqualTo(50f));
        Assert.That(settings.cameraSmoothingIntensity, Is.EqualTo(1f));
    }

    [Test]
    public void Defaults_CameraEffectIntensitiesStartAtFullStrength()
    {
        GameSettingsData settings = GameSettingsData.CreateDefaults(1920, 1080, 0);

        Assert.That(settings.cameraShakeIntensity, Is.EqualTo(1f));
        Assert.That(settings.headBobIntensity, Is.EqualTo(1f));
        Assert.That(settings.cameraRollIntensity, Is.EqualTo(1f));
        Assert.That(settings.strafeLeanIntensity, Is.EqualTo(1f));
        Assert.That(settings.breathingIntensity, Is.EqualTo(1f));
    }

    [Test]
    public void Sanitize_ClampsCameraEffectIntensities()
    {
        GameSettingsData settings = GameSettingsData.CreateDefaults(1920, 1080, 0);
        settings.cameraShakeIntensity = 4f;
        settings.headBobIntensity = -1f;
        settings.cameraRollIntensity = 0.5f;
        settings.strafeLeanIntensity = 1f;
        settings.breathingIntensity = 1.0001f;

        settings.Sanitize(3);

        Assert.That(settings.cameraShakeIntensity, Is.EqualTo(1f));
        Assert.That(settings.headBobIntensity, Is.EqualTo(0f));
        Assert.That(settings.cameraRollIntensity, Is.EqualTo(0.5f));
        Assert.That(settings.strafeLeanIntensity, Is.EqualTo(1f));
        Assert.That(settings.breathingIntensity, Is.EqualTo(1f));
    }

    // A settings file written before these existed must read back as a camera that behaves
    // the way it always did. The float default of zero would silently switch every effect
    // off for everyone who already played.
    [Test]
    public void OlderJson_LeavesCameraEffectIntensitiesAtFullStrength()
    {
        GameSettingsData defaults = GameSettingsData.CreateDefaults(1920, 1080, 0);

        bool success = GameSettingsStorage.TryDeserialize(
            "{\"version\":3,\"fieldOfView\":90}",
            defaults,
            3,
            out GameSettingsData migrated);

        Assert.That(success, Is.True);
        Assert.That(migrated.version, Is.EqualTo(GameSettingsData.CurrentVersion));
        Assert.That(migrated.fieldOfView, Is.EqualTo(90f).Within(0.001f));
        Assert.That(migrated.cameraShakeIntensity, Is.EqualTo(1f));
        Assert.That(migrated.headBobIntensity, Is.EqualTo(1f));
        Assert.That(migrated.cameraRollIntensity, Is.EqualTo(1f));
        Assert.That(migrated.strafeLeanIntensity, Is.EqualTo(1f));
        Assert.That(migrated.breathingIntensity, Is.EqualTo(1f));
    }

    [Test]
    public void FrameRateLimit_SnapsToDropdownOptions()
    {
        // Старый слайдер мог сохранить любое число из 30..1000 — Dropdown обязан найти свой индекс.
        Assert.That(GameSettingsData.NearestFrameRateLimit(75), Is.EqualTo(60));
        Assert.That(GameSettingsData.NearestFrameRateLimit(130), Is.EqualTo(120));
        Assert.That(GameSettingsData.NearestFrameRateLimit(1000), Is.EqualTo(240));
        Assert.That(GameSettingsData.NearestFrameRateLimit(0), Is.EqualTo(-1));
        Assert.That(GameSettingsData.NearestFrameRateLimit(-1), Is.EqualTo(-1));

        GameSettingsData defaults = GameSettingsData.CreateDefaults(1920, 1080, 0);
        defaults.Sanitize(3);
        Assert.That(GameSettingsData.FrameRateLimits, Contains.Item(defaults.frameRateLimit));
    }

    [Test]
    public void TryDeserialize_PreservesDefaultsForMissingFields()
    {
        GameSettingsData defaults = GameSettingsData.CreateDefaults(2560, 1440, 2);
        defaults.musicVolume = 0.75f;

        bool success = GameSettingsStorage.TryDeserialize(
            "{\"masterVolume\":0.25}",
            defaults,
            4,
            out GameSettingsData result);

        Assert.That(success, Is.True);
        Assert.That(result.masterVolume, Is.EqualTo(0.25f).Within(0.001f));
        Assert.That(result.musicVolume, Is.EqualTo(0.75f).Within(0.001f));
        Assert.That(result.resolutionWidth, Is.EqualTo(2560));
        Assert.That(result.resolutionHeight, Is.EqualTo(1440));
    }

    [Test]
    public void TryDeserialize_RejectsBrokenJson()
    {
        GameSettingsData defaults = GameSettingsData.CreateDefaults(1920, 1080, 1);

        bool success = GameSettingsStorage.TryDeserialize(
            "{ definitely not json",
            defaults,
            3,
            out GameSettingsData result);

        Assert.That(success, Is.False);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void SaveAndLoad_RoundTripsSettings()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "WIAM-SettingsTests",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");

        try
        {
            GameSettingsStorage storage = new GameSettingsStorage(path);
            GameSettingsData source = GameSettingsData.CreateDefaults(1920, 1080, 2);
            source.masterVolume = 0.42f;
            source.cameraSmoothing = true;
            source.cameraSmoothingIntensity = 0.7f;
            source.debugShowScene = true;

            storage.Save(source, 4);
            GameSettingsData loaded = storage.Load(
                GameSettingsData.CreateDefaults(1280, 720, 0),
                4);

            Assert.That(loaded.masterVolume, Is.EqualTo(0.42f).Within(0.001f));
            Assert.That(loaded.cameraSmoothing, Is.True);
            Assert.That(loaded.cameraSmoothingIntensity, Is.EqualTo(0.7f).Within(0.001f));
            Assert.That(loaded.debugShowScene, Is.True);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Test]
    public void Load_UsesBackupWhenPrimaryFileIsDamaged()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "settings.json");

        try
        {
            GameSettingsStorage storage = new GameSettingsStorage(path);
            GameSettingsData source = GameSettingsData.CreateDefaults(1920, 1080, 1);
            source.masterVolume = 0.31f;
            storage.Save(source, 3);
            File.Copy(path, path + ".bak", true);
            File.WriteAllText(path, "not json");

            GameSettingsData loaded = storage.Load(GameSettingsData.CreateDefaults(1280, 720, 0), 3);

            Assert.That(loaded.masterVolume, Is.EqualTo(0.31f).Within(0.001f));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Test]
    public void V1Json_MigratesByKeepingDefaultsForNewFields()
    {
        GameSettingsData defaults = GameSettingsData.CreateDefaults(1920, 1080, 0);
        defaults.debugNoClipSpeed = 14f;

        bool success = GameSettingsStorage.TryDeserialize(
            "{\"version\":1,\"masterVolume\":0.5}",
            defaults,
            3,
            out GameSettingsData migrated);

        Assert.That(success, Is.True);
        Assert.That(migrated.version, Is.EqualTo(GameSettingsData.CurrentVersion));
        Assert.That(migrated.masterVolume, Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(migrated.debugNoClipSpeed, Is.EqualTo(14f).Within(0.001f));
    }

    [Test]
    public void Service_ImmediateSettingsPersistAndPublishOnlyChangedMusicGain()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "settings.json");
        GameObject gameObject = new GameObject("SettingsService Test");

        try
        {
            SettingsService service = gameObject.AddComponent<SettingsService>();
            service.InitializeForTests(path, GameSettingsData.CreateDefaults(1920, 1080, 0), 3);
            int events = 0;
            int settingsEvents = 0;
            float gain = -1f;
            service.MusicGainChanged += value => { events++; gain = value; };
            service.SettingsChanged += () => settingsEvents++;

            service.SetEffectsVolume(0.1f);
            service.SetMasterVolume(0.5f);
            service.SetMusicVolume(0.5f);
            service.Flush();

            Assert.That(events, Is.EqualTo(2));
            Assert.That(settingsEvents, Is.EqualTo(3));
            Assert.That(gain, Is.EqualTo(0.25f).Within(0.001f));
            GameSettingsData loaded = new GameSettingsStorage(path).Load(GameSettingsData.CreateDefaults(1920, 1080, 0), 3);
            Assert.That(loaded.effectsVolume, Is.EqualTo(0.1f).Within(0.001f));
            Assert.That(loaded.masterVolume, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(loaded.musicVolume, Is.EqualTo(0.5f).Within(0.001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
            DeleteTemporaryDirectory(directory);
        }
    }

    [Test]
    public void Service_DraftCameraSettingsDoNotChangeCurrentUntilApply()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "settings.json");
        GameObject gameObject = new GameObject("SettingsService Test");

        try
        {
            SettingsService service = gameObject.AddComponent<SettingsService>();
            service.InitializeForTests(path, GameSettingsData.CreateDefaults(1920, 1080, 0), 3);
            float originalFov = service.Current.fieldOfView;
            ISettingsEditSession edit = service.BeginEdit();
            edit.Draft.fieldOfView = 95f;
            edit.Draft.invertVerticalLook = true;
            edit.Draft.cameraSmoothing = true;
            edit.Cancel();

            Assert.That(service.Current.fieldOfView, Is.EqualTo(originalFov));
            Assert.That(service.Current.invertVerticalLook, Is.False);
            Assert.That(service.Current.cameraSmoothing, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
            DeleteTemporaryDirectory(directory);
        }
    }

    [Test]
    public void Service_ResetThenApplyRestoresDefaults()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "settings.json");
        GameObject gameObject = new GameObject("SettingsService Test");

        try
        {
            SettingsService service = gameObject.AddComponent<SettingsService>();
            service.InitializeForTests(path, GameSettingsData.CreateDefaults(1920, 1080, 0), 3);
            service.SetMasterVolume(0.2f);
            ISettingsEditSession edit = service.BeginEdit();
            edit.Draft.fieldOfView = 100f;
            edit.ResetToDefaults();
            edit.Apply();

            Assert.That(service.Current.masterVolume, Is.EqualTo(1f));
            Assert.That(service.Current.fieldOfView, Is.EqualTo(75f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
            DeleteTemporaryDirectory(directory);
        }
    }

    [Test]
    public void Service_CameraEffectIntensitiesClampCommitAndPersist()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "settings.json");
        GameObject gameObject = new GameObject("SettingsService Test");

        try
        {
            SettingsService service = gameObject.AddComponent<SettingsService>();
            service.InitializeForTests(path, GameSettingsData.CreateDefaults(1920, 1080, 0), 3);
            int settingsEvents = 0;
            service.SettingsChanged += () => settingsEvents++;

            service.SetCameraShakeIntensity(0.25f);
            service.SetHeadBobIntensity(0f);
            service.SetCameraRollIntensity(5f);
            service.SetStrafeLeanIntensity(-1f);
            service.SetBreathingIntensity(0.5f);
            service.Flush();

            Assert.That(settingsEvents, Is.EqualTo(4), "A value already at its default announces nothing.");
            Assert.That(service.Current.cameraShakeIntensity, Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(service.Current.headBobIntensity, Is.EqualTo(0f));
            Assert.That(service.Current.cameraRollIntensity, Is.EqualTo(1f));
            Assert.That(service.Current.strafeLeanIntensity, Is.EqualTo(0f));
            Assert.That(service.Current.breathingIntensity, Is.EqualTo(0.5f).Within(0.001f));

            GameSettingsData loaded = new GameSettingsStorage(path)
                .Load(GameSettingsData.CreateDefaults(1920, 1080, 0), 3);
            Assert.That(loaded.cameraShakeIntensity, Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(loaded.headBobIntensity, Is.EqualTo(0f));
            Assert.That(loaded.breathingIntensity, Is.EqualTo(0.5f).Within(0.001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
            DeleteTemporaryDirectory(directory);
        }
    }

    // The settings screen writes a draft and waits for Apply, so nothing a player drags is
    // allowed to reach the camera until they press it.
    [Test]
    public void Service_DraftCameraEffectIntensitiesWaitForApply()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "settings.json");
        GameObject gameObject = new GameObject("SettingsService Test");

        try
        {
            SettingsService service = gameObject.AddComponent<SettingsService>();
            service.InitializeForTests(path, GameSettingsData.CreateDefaults(1920, 1080, 0), 3);

            ISettingsEditSession cancelled = service.BeginEdit();
            cancelled.Draft.cameraShakeIntensity = 0f;
            cancelled.Cancel();

            Assert.That(service.Current.cameraShakeIntensity, Is.EqualTo(1f));

            ISettingsEditSession applied = service.BeginEdit();
            applied.Draft.cameraShakeIntensity = 0f;
            applied.Draft.headBobIntensity = 0.4f;
            applied.Apply();

            Assert.That(service.Current.cameraShakeIntensity, Is.EqualTo(0f));
            Assert.That(service.Current.headBobIntensity, Is.EqualTo(0.4f).Within(0.001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
            DeleteTemporaryDirectory(directory);
        }
    }

    [Test]
    public void Service_DefaultsRestoreCameraEffectIntensities()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "settings.json");
        GameObject gameObject = new GameObject("SettingsService Test");

        try
        {
            SettingsService service = gameObject.AddComponent<SettingsService>();
            service.InitializeForTests(path, GameSettingsData.CreateDefaults(1920, 1080, 0), 3);
            service.SetCameraShakeIntensity(0f);
            service.SetHeadBobIntensity(0.2f);

            ISettingsEditSession edit = service.BeginEdit();
            edit.ResetToDefaults();
            edit.Apply();

            Assert.That(service.Current.cameraShakeIntensity, Is.EqualTo(1f));
            Assert.That(service.Current.headBobIntensity, Is.EqualTo(1f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "WIAM-SettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}
