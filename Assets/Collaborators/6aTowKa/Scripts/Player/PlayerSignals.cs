using System.Collections.Generic;
using UnityEngine;

public class PlayerSignals
{
    public readonly List<object> SignalsList = new List<object>();

    public readonly PlayerSignal<Vector2> MoveSignal;
    public readonly PlayerSignal CrouchInputSignal;
    public readonly PlayerSignal<bool> CrouchUpdateSignal;
    public readonly PlayerSignal<bool> CrouchSyncSignal;
    public readonly PlayerSignal<Sprite> CrosshairSpriteSignal;
    public readonly PlayerSignal Interact;
    public readonly PlayerSignal Uninteract;
    public readonly PlayerSignal PickUp;
    public readonly PlayerSignal Drop;

    public PlayerSignals()
    {
        MoveSignal = new(SignalsList, nameof(MoveSignal));
        CrouchInputSignal = new(SignalsList, nameof(CrouchInputSignal));
        CrouchUpdateSignal = new(SignalsList, nameof(CrouchUpdateSignal));
        CrouchSyncSignal = new(SignalsList, nameof(CrouchSyncSignal));
        CrosshairSpriteSignal = new(SignalsList, nameof(CrosshairSpriteSignal));
        Interact = new(SignalsList, nameof(Interact));
        Uninteract = new(SignalsList, nameof(Uninteract));
        PickUp = new(SignalsList, nameof(PickUp));
        Drop = new(SignalsList, nameof(Drop));
    }
}
