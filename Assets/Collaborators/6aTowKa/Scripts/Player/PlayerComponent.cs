using Unity.Netcode;
using UnityEngine;

public abstract class PlayerComponent : MonoBehaviour
{
    protected PlayerSignals signals;

    public void Init(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner)
    {
        signals = orch.Signals;
        OnPostInit(orch, isMultiplayer, isOwner);
    }

    protected virtual void OnPostInit(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner) { }
}

public abstract class PlayerNetworkComponent : NetworkBehaviour
{
    protected PlayerSignals signals;

    public void Init(PlayerOrchestrator orch)
    {
        signals = orch.Signals;
        OnPostInit(orch);
    }

    protected virtual void OnPostInit(PlayerOrchestrator orch) { }
}