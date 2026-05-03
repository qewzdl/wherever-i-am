using UnityEngine;

public class PlayerSignals
{
    public readonly PlayerSignal<Vector2> MoveSignal = new();
    public readonly PlayerSignal CrouchInputSignal = new();
    public readonly PlayerSignal<bool> CrouchUpdateSignal = new();
    public readonly PlayerSignal<bool> CrouchSyncSignal = new("SyncCrouch");
}