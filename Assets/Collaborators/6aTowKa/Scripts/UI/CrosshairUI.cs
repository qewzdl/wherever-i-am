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
        baseSize = crosshairImage.rectTransform.sizeDelta;
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
