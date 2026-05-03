using UnityEngine;

public class PlayerOrchestrator : MonoBehaviour
{
    public readonly PlayerSignals Signals = new PlayerSignals();

    public void Setup(bool isMultiplayer, bool isOwner)
    {
        foreach (PlayerComponent playerComponent in GetComponents<PlayerComponent>())
        {
            if (playerComponent.enabled)
                playerComponent.Init(this, isMultiplayer, isOwner);
        }

        foreach (PlayerNetworkComponent playerComponent in GetComponents<PlayerNetworkComponent>())
        {
            if (playerComponent.enabled)
                playerComponent.Init(this);
        }
    }

    private void Cleanup()
    {
        foreach (IPlayerSignalListener playerComponent in GetComponents<IPlayerSignalListener>())
        {
            playerComponent.Cleanup();
        }
    }

    private void OnDestroy()
    {
        Cleanup();
    }
}
