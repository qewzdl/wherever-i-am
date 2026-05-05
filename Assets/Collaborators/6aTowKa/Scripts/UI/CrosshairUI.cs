using UnityEngine;
using UnityEngine.UI;

public class CrosshairUI : MonoBehaviour
{
    [SerializeField] private Image crosshairImage;

    public void UpdateCrosshairSprite(Sprite sprite)
    {
        crosshairImage.sprite = sprite;
    }
}
