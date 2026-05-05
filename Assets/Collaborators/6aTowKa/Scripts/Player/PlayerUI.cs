using UnityEngine;

public class PlayerUI : PlayerComponent, IPlayerSignalListener
{
    private CrosshairUI crosshairUI;

    protected override void OnPostInit(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner)
    {
        crosshairUI = FindFirstObjectByType<CrosshairUI>();
        signals.CrosshairSpriteSignal.Listen(UpdateCrosshairSprite);
    }

    public void Cleanup()
    {
        signals.CrosshairSpriteSignal.Unlisten(UpdateCrosshairSprite);
    }

    private void UpdateCrosshairSprite(Sprite sprite)
    {
        crosshairUI.UpdateCrosshairSprite(sprite);
    }

}
