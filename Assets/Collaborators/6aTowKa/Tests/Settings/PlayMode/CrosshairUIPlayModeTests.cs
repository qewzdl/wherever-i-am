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
    private const float RestingHeight = 40f;
    private const float ActionHeight = 96f;

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
        CrosshairStyle style = CreateStyle(null);
        // CrosshairUI validates its references in OnEnable, which runs the
        // moment the component lands on an active object - before the test can
        // inject the fields.
        hud.SetActive(false);

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

            CrosshairUI crosshair = hud.AddComponent<CrosshairUI>();
            SetPrivate(crosshair, "crosshairImage", image);
            SetPrivate(crosshair, "style", style);
            crosshair.Construct(service);
            hud.SetActive(true);

            service.SetCrosshairSize(1.5f);
            yield return null;
            Assert.That(image.rectTransform.sizeDelta.x, Is.EqualTo(RestingHeight * 1.5f).Within(0.01f));

            hud.SetActive(false);          // Пауза: HUD скрыт.
            service.SetCrosshairSize(1f);  // Игрок двигает слайдер, пока HUD скрыт.
            hud.SetActive(true);           // Продолжение игры.
            yield return null;

            Assert.That(image.rectTransform.sizeDelta.x, Is.EqualTo(RestingHeight).Within(0.01f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(hud);
            UnityEngine.Object.DestroyImmediate(serviceObject);
            UnityEngine.Object.DestroyImmediate(style);
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    // The whole point of the asset: a crosshair small enough to aim past and
    // an action icon big enough to read are the same box at two sizes, and
    // which one applies is decided by whether anything is offering an action.
    [Test]
    public void Style_SizesTheRestingCrosshairAndTheActionIconApart()
    {
        CrosshairStyle style = CreateStyle(null);

        try
        {
            CrosshairPose resting = style.Resolve(null);
            Assert.That(resting.Size.y, Is.EqualTo(RestingHeight).Within(0.01f));

            Sprite action = CreateSprite(64, 64);

            try
            {
                CrosshairPose offered = style.Resolve(action);
                Assert.That(offered.Sprite, Is.SameAs(action));
                Assert.That(offered.Size.y, Is.EqualTo(ActionHeight).Within(0.01f));
            }
            finally
            {
                DestroySprite(action);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(style);
        }
    }

    // Height is configured and width follows the sprite, so an icon that is
    // not square does not get squashed into one.
    [Test]
    public void Style_KeepsAnActionIconsAspect()
    {
        CrosshairStyle style = CreateStyle(null);
        Sprite wide = CreateSprite(128, 64);

        try
        {
            CrosshairPose pose = style.Resolve(wide);

            Assert.That(pose.Size.y, Is.EqualTo(ActionHeight).Within(0.01f));
            Assert.That(pose.Size.x, Is.EqualTo(ActionHeight * 2f).Within(0.01f));
        }
        finally
        {
            DestroySprite(wide);
            UnityEngine.Object.DestroyImmediate(style);
        }
    }

    // Nothing in reach and an interactable with no icon are the same thing:
    // both get the resting crosshair rather than an empty box at action size.
    [Test]
    public void Style_FallsBackToTheRestingCrosshairWhenNothingIsOffered()
    {
        Sprite resting = CreateSprite(16, 16);
        CrosshairStyle style = CreateStyle(resting);

        try
        {
            CrosshairPose pose = style.Resolve(null);

            Assert.That(pose.Sprite, Is.SameAs(resting));
            Assert.That(pose.Size.y, Is.EqualTo(RestingHeight).Within(0.01f));
        }
        finally
        {
            DestroySprite(resting);
            UnityEngine.Object.DestroyImmediate(style);
        }
    }

    private static CrosshairStyle CreateStyle(Sprite defaultSprite)
    {
        CrosshairStyle style = ScriptableObject.CreateInstance<CrosshairStyle>();
        SetPrivate(style, "defaultSprite", defaultSprite);
        SetPrivate(style, "defaultHeight", RestingHeight);
        SetPrivate(style, "actionHeight", ActionHeight);
        return style;
    }

    private static Sprite CreateSprite(int width, int height)
    {
        Texture2D texture = new Texture2D(width, height);
        return Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f));
    }

    private static void DestroySprite(Sprite sprite)
    {
        if (sprite == null)
            return;

        Texture2D texture = sprite.texture;
        UnityEngine.Object.DestroyImmediate(sprite);

        if (texture != null)
            UnityEngine.Object.DestroyImmediate(texture);
    }

    private static void SetPrivate(object target, string field, object value)
    {
        target
            .GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);
    }
}
