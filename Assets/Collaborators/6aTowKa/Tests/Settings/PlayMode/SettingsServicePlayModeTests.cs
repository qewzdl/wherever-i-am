using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class SettingsServicePlayModeTests
{
    [UnityTest]
    public IEnumerator ImmediateSensitivityAndAudio_AreAvailableOnNextFrame()
    {
        string directory = Path.Combine(Path.GetTempPath(), "WIAM-SettingsPlayMode", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        GameObject gameObject = new GameObject("Settings Service PlayMode Test");

        try
        {
            SettingsService service = gameObject.AddComponent<SettingsService>();
            service.InitializeForTests(path, GameSettingsData.CreateDefaults(1920, 1080, 0), 3);
            service.SetMouseSensitivity(77f);
            service.SetMasterVolume(0.4f);
            service.SetInterfaceVolume(0.5f);

            yield return null;

            Assert.That(service.Current.mouseSensitivity, Is.EqualTo(77f));
            Assert.That(service.Current.masterVolume, Is.EqualTo(0.4f));
            Assert.That(service.Current.interfaceVolume, Is.EqualTo(0.5f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    // The settings screen only ever writes the draft, so this is the whole of
    // how a volume reaches the music: everything else in the game asks Current
    // when it plays a sound, and the music is told once and then waits.
    [UnityTest]
    public IEnumerator ApplyingASession_TellsTheMusicItsNewGain()
    {
        string directory = Path.Combine(Path.GetTempPath(), "WIAM-SettingsPlayMode", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        GameObject gameObject = new GameObject("Settings Apply PlayMode Test");

        try
        {
            SettingsService service = gameObject.AddComponent<SettingsService>();
            service.InitializeForTests(path, GameSettingsData.CreateDefaults(1920, 1080, 0), 3);

            float heardGain = -1f;
            service.MusicGainChanged += gain => heardGain = gain;

            ISettingsEditSession session = service.BeginEdit();
            session.Draft.masterVolume = 0.5f;
            session.Draft.musicVolume = 0.5f;
            session.Apply();

            yield return null;

            Assert.That(service.Current.musicVolume, Is.EqualTo(0.5f));
            Assert.That(heardGain, Is.EqualTo(0.25f).Within(0.0001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
