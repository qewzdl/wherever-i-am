using System;
using UnityEngine;
using UnityEngine.UI;

public class CrosshairUI : MonoBehaviour
{
    [SerializeField] private Image crosshairImage;
    private Vector2 baseSize;
    private int appliedSettingsRevision = -1;

    public static CrosshairUI Active { get; private set; }
    public static event Action<CrosshairUI> ActiveChanged;

    private void Awake()
    {
        // Unscaled prefab size: must be read before Update() ever scales sizeDelta,
        // otherwise re-enabling the HUD would treat the scaled size as the new base.
        if (crosshairImage != null)
            baseSize = crosshairImage.rectTransform.sizeDelta;
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
        appliedSettingsRevision = -1; // Update() was not running while hidden; re-apply the current size.
        ActiveChanged?.Invoke(this);
    }

    private void Update()
    {
        if (!SettingsService.TryGet(out ISettingsService settings) ||
            settings.Revision == appliedSettingsRevision)
            return;

        crosshairImage.rectTransform.sizeDelta = baseSize * settings.Current.crosshairSize;
        appliedSettingsRevision = settings.Revision;
    }

    private void OnDisable()
    {
        if (Active != this)
            return;

        Active = null;
        ActiveChanged?.Invoke(null);
    }

    public void UpdateCrosshairSprite(Sprite sprite)
    {
        crosshairImage.sprite = sprite;
    }
}
