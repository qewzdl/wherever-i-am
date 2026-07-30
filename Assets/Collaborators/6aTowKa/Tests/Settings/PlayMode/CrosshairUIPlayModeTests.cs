using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class CrosshairUIPlayModeTests
{
    private const float BaseSize = 40f;

    /// <summary>
    /// Пауза скрывает HUD, а размер прицела меняется именно из меню паузы.
    /// Базовый размер не должен «съезжать» на каждом цикле скрытия/показа.
    /// </summary>
    [UnityTest]
    public IEnumerator CrosshairSize_DoesNotCompound_WhenHudIsHiddenWhileChanging()
    {
        string directory = Path.Combine(Path.GetTempPath(), "WIAM-CrosshairPlayMode", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        GameObject serviceObject = new GameObject("Settings Service");
        GameObject hud = new GameObject("HUD");

        try
        {
            SettingsService service = serviceObject.AddComponent<SettingsService>();
            service.InitializeForTests(
                Path.Combine(directory, "settings.json"),
                GameSettingsData.CreateDefaults(1920, 1080, 0),
                3);

            Image image = new GameObject("Crosshair", typeof(RectTransform), typeof(CanvasRenderer))
                .AddComponent<Image>();
            image.transform.SetParent(hud.transform, false);
            image.rectTransform.sizeDelta = new Vector2(BaseSize, BaseSize);

            CrosshairUI crosshair = hud.AddComponent<CrosshairUI>();
            typeof(CrosshairUI)
                .GetField("crosshairImage", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(crosshair, image);
            hud.SetActive(true);

            service.SetCrosshairSize(1.5f);
            yield return null;
            Assert.That(image.rectTransform.sizeDelta.x, Is.EqualTo(BaseSize * 1.5f).Within(0.01f));

            hud.SetActive(false);          // Пауза: HUD скрыт.
            service.SetCrosshairSize(1f);  // Игрок двигает слайдер, пока HUD скрыт.
            hud.SetActive(true);           // Продолжение игры.
            yield return null;

            Assert.That(image.rectTransform.sizeDelta.x, Is.EqualTo(BaseSize).Within(0.01f));
        }
        finally
        {
            UnityEngine.Object.Destroy(hud);
            UnityEngine.Object.Destroy(serviceObject);
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
