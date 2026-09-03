using System.Collections.Generic;
using UnityEngine;

public class PlayerSignals
{
    public readonly List<object> SignalsList = new List<object>();

    public readonly PlayerSignal<Vector2> MoveSignal;
    // Carries whether the crouch key is down, rather than that it was touched.
    // Which of those a press means - flip the stance, or hold it - is a setting
    // now, and only the thing that owns the stance can answer it.
    public readonly PlayerSignal<bool> CrouchInputSignal;
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
