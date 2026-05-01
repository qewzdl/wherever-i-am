using UnityEngine;
using UnityEngine.EventSystems;

public class UiButtonSound : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    [Header("Settings")]
    [SerializeField] private bool playHoverSound = true;
    [SerializeField] private bool playClickSound = true;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!playHoverSound) return;

        if (AudioManager.Instance == null) return;

        AudioManager.Instance.UI.PlayHover();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!playClickSound) return;

        if (AudioManager.Instance == null) return;

        AudioManager.Instance.UI.PlayClick();
    }
}