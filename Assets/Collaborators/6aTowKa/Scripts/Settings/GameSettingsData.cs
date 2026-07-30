using System;
using UnityEngine;

[Serializable]
public sealed class GameSettingsData
{
    public const int CurrentVersion = 2;

    /// <summary>Единственный источник правды для слайдера и камеры.</summary>
    public const float MinMouseSensitivity = 10f;
    public const float MaxMouseSensitivity = 100f;

    /// <summary>Допустимые значения лимита кадров в порядке отображения; -1 — без лимита.</summary>
    public static readonly int[] FrameRateLimits = { 30, 60, 90, 120, 144, 165, 240, -1 };

    public int version = CurrentVersion;

    public int resolutionWidth = 1920;
    public int resolutionHeight = 1080;
    public int fullScreenMode = (int)FullScreenMode.FullScreenWindow;
    public int qualityLevel;
    public bool verticalSync = true;
    public int frameRateLimit = 60;

    public float masterVolume = 1f;
    public float musicVolume = 1f;
    public float effectsVolume = 1f;
    public float interfaceVolume = 1f;
    public float interfaceOpacity = 1f;
    public float crosshairSize = 1f;

    public float mouseSensitivity = 100f;
    public bool invertVerticalLook;

    public float fieldOfView = 75f;
    public bool cameraSmoothing;
    public float cameraSmoothingIntensity = 0.35f;

    public bool debugShowPerformance = true;
    public bool debugShowPlayer = true;
    public bool debugShowNetwork = true;
    public bool debugShowEnemies = true;
    public bool debugShowScene;
    public float debugNoClipSpeed = 10f;

    public static GameSettingsData CreateDefaults(
        int resolutionWidth,
        int resolutionHeight,
        int qualityLevel)
    {
        return new GameSettingsData
        {
            resolutionWidth = Mathf.Max(640, resolutionWidth),
            resolutionHeight = Mathf.Max(360, resolutionHeight),
            qualityLevel = Mathf.Max(0, qualityLevel)
        };
    }

    public GameSettingsData Clone()
    {
        return JsonUtility.FromJson<GameSettingsData>(JsonUtility.ToJson(this));
    }

    public void CopyFrom(GameSettingsData source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), this);
    }

    public bool HasSameDisplaySettings(GameSettingsData other)
    {
        return other != null &&
               resolutionWidth == other.resolutionWidth &&
               resolutionHeight == other.resolutionHeight &&
               fullScreenMode == other.fullScreenMode;
    }

    public void CopyDisplaySettingsFrom(GameSettingsData source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        resolutionWidth = source.resolutionWidth;
        resolutionHeight = source.resolutionHeight;
        fullScreenMode = source.fullScreenMode;
    }

    /// <summary>
    /// Приводит значение к ближайшему пункту <see cref="FrameRateLimits"/>, чтобы Dropdown
    /// всегда находил свой индекс — в том числе для настроек, сохранённых старым слайдером.
    /// </summary>
    public static int NearestFrameRateLimit(int value)
    {
        if (value <= 0)
        {
            return -1;
        }

        int nearest = FrameRateLimits[0];

        foreach (int limit in FrameRateLimits)
        {
            if (limit > 0 && Mathf.Abs(limit - value) < Mathf.Abs(nearest - value))
            {
                nearest = limit;
            }
        }

        return nearest;
    }

    public void Sanitize(int qualityLevelCount)
    {
        version = CurrentVersion;

        resolutionWidth = Mathf.Clamp(resolutionWidth, 640, 16384);
        resolutionHeight = Mathf.Clamp(resolutionHeight, 360, 8640);

        if (!Enum.IsDefined(typeof(FullScreenMode), fullScreenMode))
        {
            fullScreenMode = (int)FullScreenMode.FullScreenWindow;
        }

        qualityLevel = Mathf.Clamp(qualityLevel, 0, Mathf.Max(0, qualityLevelCount - 1));
        frameRateLimit = NearestFrameRateLimit(frameRateLimit);

        masterVolume = Mathf.Clamp01(masterVolume);
        musicVolume = Mathf.Clamp01(musicVolume);
        effectsVolume = Mathf.Clamp01(effectsVolume);
        interfaceVolume = Mathf.Clamp01(interfaceVolume);
        interfaceOpacity = Mathf.Clamp01(interfaceOpacity);
        crosshairSize = Mathf.Clamp(crosshairSize, 0.5f, 2f);

        mouseSensitivity = Mathf.Clamp(mouseSensitivity, MinMouseSensitivity, MaxMouseSensitivity);
        fieldOfView = Mathf.Clamp(fieldOfView, 50f, 110f);
        cameraSmoothingIntensity = Mathf.Clamp01(cameraSmoothingIntensity);
        debugNoClipSpeed = Mathf.Clamp(debugNoClipSpeed, 2f, 30f);
    }
}
