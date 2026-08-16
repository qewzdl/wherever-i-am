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
