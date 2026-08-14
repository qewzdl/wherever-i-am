using System;
using UnityEngine;
using UnityEngine.UI;

public class CrosshairUI : MonoBehaviour, ISettingsServiceConsumer
{
    [SerializeField] private Image crosshairImage;
    private Vector2 baseSize;
    private bool baseSizeCaptured;
    private ISettingsService settingsService;

    public static CrosshairUI Active { get; private set; }
    public static event Action<CrosshairUI> ActiveChanged;

    private void Awake()
    {
        // Unscaled prefab size: must be read before Update() ever scales sizeDelta,
        // otherwise re-enabling the HUD would treat the scaled size as the new base.
        CaptureBaseSize();
    }

    private void OnEnable()
    {
        if (crosshairImage == null)
        {
            Debug.LogError($"{nameof(CrosshairUI)} is missing {nameof(crosshairImage)}.", this);
            enabled = false;
            return;
        }

        if (Active != null && Active != this)
            Debug.LogWarning($"Replacing active {nameof(CrosshairUI)} '{Active.name}' with '{name}'.", this);

        Active = this;
        ActiveChanged?.Invoke(this);
        ApplySettings();
    }

    public void Construct(ISettingsService settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        if (ReferenceEquals(settingsService, settings))
            return;

        ReleaseSettingsService();
        settingsService = settings;
        settingsService.SettingsChanged += ApplySettings;
        CaptureBaseSize();
        ApplySettings();
    }

    public void ReleaseSettingsService()
    {
        if (settingsService == null)
            return;

        settingsService.SettingsChanged -= ApplySettings;
        settingsService = null;
    }

    private void OnDisable()
    {
        if (Active != this)
            return;

        Active = null;
        ActiveChanged?.Invoke(null);
    }

    private void OnDestroy()
    {
        ReleaseSettingsService();
    }

    private void CaptureBaseSize()
    {
        if (baseSizeCaptured || crosshairImage == null)
            return;

        baseSize = crosshairImage.rectTransform.sizeDelta;
        baseSizeCaptured = true;
    }

    private void ApplySettings()
    {
        if (settingsService == null || crosshairImage == null)
            return;

        CaptureBaseSize();
        crosshairImage.rectTransform.sizeDelta = baseSize * settingsService.Current.crosshairSize;
    }

    public void UpdateCrosshairSprite(Sprite sprite)
    {
        crosshairImage.sprite = sprite;
    }
}
