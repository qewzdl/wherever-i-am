using UnityEngine;
using UnityEngine.EventSystems;

public class UiButtonSound : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler, IUiSoundServiceConsumer
{
    [Header("Settings")]
    [SerializeField] private bool playHoverSound = true;
    [SerializeField] private bool playClickSound = true;

    private IUiSoundService uiSoundService;

    public void Construct(IUiSoundService service)
    {
        uiSoundService = service;
    }

    public void ReleaseUiSoundService()
    {
        uiSoundService = null;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!playHoverSound) return;

        uiSoundService?.PlayHover();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!playClickSound) return;

        uiSoundService?.PlayClick();
    }
}
