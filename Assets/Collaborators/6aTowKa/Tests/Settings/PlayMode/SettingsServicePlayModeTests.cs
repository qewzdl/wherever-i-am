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
            UnityEngine.Object.Destroy(gameObject);
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
