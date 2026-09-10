using System;
using UnityEngine;
using UnityEngine.UI;

public class CrosshairUI : MonoBehaviour, ISettingsServiceConsumer
{
    [SerializeField] private Image crosshairImage;

    // Both pictures and both sizes, in one asset. Nothing about how the
    // crosshair looks is decided here or on a prefab any more.
    [SerializeField] private CrosshairStyle style;

    // What the player is looking at, or null for nothing. Kept because the
    // size has two reasons to be recomputed - the player moved their eyes, or
    // the player moved the slider - and each of them only knows about itself.
    private Sprite interactionSprite;

    private ISettingsService settingsService;

    public static CrosshairUI Active { get; private set; }
    public static event Action<CrosshairUI> ActiveChanged;

    private void OnEnable()
    {
        if (crosshairImage == null)
        {
            Debug.LogError($"{nameof(CrosshairUI)} is missing {nameof(crosshairImage)}.", this);
            enabled = false;
            return;
        }

        if (style == null)
        {
            Debug.LogError($"{nameof(CrosshairUI)} is missing {nameof(style)}.", this);
            enabled = false;
            return;
        }

        if (Active != null && Active != this)
            Debug.LogWarning($"Replacing active {nameof(CrosshairUI)} '{Active.name}' with '{name}'.", this);

        Active = this;
        ActiveChanged?.Invoke(this);
        Apply();
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
        Apply();
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

    // Null means nothing in reach. The resting crosshair is not something a
    // caller has to know the picture of - the style holds it - so the only
    // thing anybody has to say here is what the player is looking at.
    public void ShowInteraction(Sprite sprite)
    {
        interactionSprite = sprite;
        Apply();
    }

    private void ApplySettings()
    {
        Apply();
    }

    // Both halves at once, from the same resolve. The size used to be the
    // prefab's own sizeDelta multiplied in place, which had to be captured
    // before anything scaled it and re-captured whenever the component was
    // reconstructed - a base that could drift because it lived in the thing it
    // was being written to. It comes from the asset now, so scaling twice is
    // not a mistake that can be made.
    private void Apply()
    {
        if (crosshairImage == null || style == null)
            return;

        CrosshairPose pose = style.Resolve(interactionSprite);
        float playerScale = settingsService != null
            ? Mathf.Max(0f, settingsService.Current.crosshairSize)
            : 1f;

        crosshairImage.sprite = pose.Sprite;
        crosshairImage.enabled = pose.Sprite != null;
        crosshairImage.rectTransform.sizeDelta = pose.Size * playerScale;
    }
}
