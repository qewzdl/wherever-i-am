using UnityEngine;

public class PlayerUI :
    PlayerComponent,
    IPlayerSignalListener,
    ILocalPlayerPresentationService
{
    private CrosshairUI crosshairUI;

    public bool IsPresentationActive => isActiveAndEnabled;

    protected override void OnPostInit(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner)
    {
        BindCrosshair(CrosshairUI.Active);
        CrosshairUI.ActiveChanged += BindCrosshair;
        signals.CrosshairSpriteSignal.Listen(UpdateCrosshairSprite);
    }

    public void Cleanup()
    {
        CrosshairUI.ActiveChanged -= BindCrosshair;
        signals.CrosshairSpriteSignal.Unlisten(UpdateCrosshairSprite);
        crosshairUI = null;
    }

    private void BindCrosshair(CrosshairUI activeCrosshair)
    {
        crosshairUI = activeCrosshair;
    }

    private void UpdateCrosshairSprite(Sprite sprite)
    {
        if (crosshairUI == null)
            return;

        crosshairUI.UpdateCrosshairSprite(sprite);
    }
}
