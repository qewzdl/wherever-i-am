using Unity.Netcode;
using UnityEngine;

public abstract class PlayerComponent : MonoBehaviour
{
    protected PlayerSignals signals;
    protected PlayerStates states;

    public void Init(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner)
    {
        signals = orch.Signals;
        states = orch.States;
        OnPostInit(orch, isMultiplayer, isOwner);
    }

    protected virtual void OnPostInit(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner) { }
}

public abstract class PlayerNetworkComponent : NetworkBehaviour
{
    protected PlayerSignals signals;
    protected PlayerStates states;

    public void Init(PlayerOrchestrator orch)
    {
        signals = orch.Signals;
        states = orch.States;
        OnPostInit(orch);
    }

    protected virtual void OnPostInit(PlayerOrchestrator orch) { }
}
