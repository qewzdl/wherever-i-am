using UnityEngine;

// How hard a body has been working, judged from nothing but where it has been.
//
// Pulled out of the validator that uses it because the netcode around that -
// am I the server, is this spawned, does this emitter belong to me - is
// boilerplate that cannot be subtly wrong, while this is arithmetic that can.
// A model that drained on walking, or that never drained at all, would refuse
// or permit every breath in the game and look exactly the same from outside.
//
// Deliberately cruder than the tank the player actually carries. Crouching
// still and standing still are the same thing from out here, and the recovery
// curve and the per-condition rates are invisible, so this fills at the
// plainest rate there is. That makes it forgive rather than accuse, which is
// the right way round for something whose only job is to refuse claims nobody
// could honestly make.
public sealed class ObservedExertion
{
    private Vector3 previousPosition;
    private float previousSampleTime;
    private bool hasPreviousSample;

    public float Stamina { get; private set; } = 1f;

    public void Reset()
    {
        Stamina = 1f;
        hasPreviousSample = false;
        previousPosition = Vector3.zero;
        previousSampleTime = 0f;
    }

    // The first sample of a life only establishes where the body is; there is
    // no interval behind it to measure anything over.
    public void Sample(Vector3 position, float time, PlayerMovementProfile movement)
    {
        if (movement == null)
            return;

        if (!hasPreviousSample)
        {
            previousPosition = position;
            previousSampleTime = time;
            hasPreviousSample = true;
            return;
        }

        float deltaTime = time - previousSampleTime;

        previousSampleTime = time;
        Vector3 displacement = position - previousPosition;
        previousPosition = position;
        displacement.y = 0f;

        if (deltaTime <= Mathf.Epsilon)
            return;

        PlayerGait gait = movement.GaitFromObservedSpeed(displacement.magnitude / deltaTime);

        // Only running costs anything. Walking is below the running band by
        // construction, so somebody who has been walking can never arrive at a
        // tank empty enough to be short of breath - which is the whole claim
        // this exists to be able to refuse.
        Stamina = gait == PlayerGait.Running
            ? Mathf.Max(0f, Stamina - deltaTime / movement.RunSeconds)
            : Mathf.Min(1f, Stamina + deltaTime / movement.RecoverySeconds);
    }
}
