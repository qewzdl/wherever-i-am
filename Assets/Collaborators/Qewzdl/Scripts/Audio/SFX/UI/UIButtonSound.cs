using UnityEngine;
using UnityEngine.EventSystems;

public class UiButtonSound : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler, IUiSoundServiceConsumer
{
    [Header("Settings")]
    [SerializeField] private bool playHoverSound = true;
    [SerializeField] private bool playClickSound = true;

    private IUiSoundService uiSoundService;

    // Asked for on first use: this is a leaf, and whoever owns it may never
    // have thought to hand it anything.
    private IUiSoundService ResolvedUiSoundService => uiSoundService ??= AudioServices.Ui();

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

        ResolvedUiSoundService?.PlayHover();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!playClickSound) return;

        ResolvedUiSoundService?.PlayClick();
    }
}
