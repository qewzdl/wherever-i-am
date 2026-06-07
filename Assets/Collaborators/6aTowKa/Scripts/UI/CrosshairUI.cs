using System;
using UnityEngine;
using UnityEngine.UI;

public class CrosshairUI : MonoBehaviour
{
    [SerializeField] private Image crosshairImage;

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
        ActiveChanged?.Invoke(this);
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
