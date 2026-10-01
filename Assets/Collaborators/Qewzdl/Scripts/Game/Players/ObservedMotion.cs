using UnityEngine;

// How a player's body is moving, as anybody watching can tell: how far it
// went since the last look, and how fast it has been going over a short
// window. Worked out once a frame and read by everything that needs it - the
// footsteps, and the server's idea of how tired somebody is - where each of
// them used to keep its own positions, its own clock and its own window.
public sealed class ObservedMotion
{
    // Seconds the pace is judged over. Somebody else's player moves in network
    // ticks, so a single frame of them often shows no movement at all; this
    // has to cover a few ticks. The footsteps judged over this and tiredness
    // over a quarter of a second; one window, the shorter, so a step still
    // starts as soon as the walking does.
    public const float Window = 0.15f;

    private readonly WindowedSpeed windowed = new();
    private Vector3 previousPosition;
    private float previousTime;
    private int sampledFrame = int.MinValue;
    private bool hasPrevious;

    // Across the ground, since the last look.
    public float Travelled { get; private set; }
    public float DeltaTime { get; private set; }

    // The average over the last whole window, held until the next.
    public float Speed { get; private set; }

    public void Reset()
    {
        windowed.Reset();
        previousPosition = Vector3.zero;
        previousTime = 0f;
        sampledFrame = int.MinValue;
        hasPrevious = false;
        Travelled = 0f;
        DeltaTime = 0f;
        Speed = 0f;
    }

    // Once a frame, by whoever asks first. Asking again in the same frame
    // changes nothing, so the second reader sees what the first did rather
    // than a look that found the body had not moved since.
    //
    // standingStill: the body is being put somewhere rather than walked
    // there, and is taken to be standing where it now is. Time still passes -
    // a player in a hiding place gets their breath back - but the jump is not
    // a distance anybody travelled.
    public void Sample(int frame, Vector3 position, float time, bool standingStill = false)
    {
        if (frame == sampledFrame)
            return;

        sampledFrame = frame;
        Travelled = 0f;
        DeltaTime = 0f;

        if (standingStill)
        {
            DeltaTime = hasPrevious ? time - previousTime : 0f;
            previousPosition = position;
            previousTime = time;
            hasPrevious = true;
            windowed.Reset();
            Speed = 0f;
            return;
        }

        // The first look only finds where the body is; there is no interval
        // behind it to measure anything over.
        if (!hasPrevious)
        {
            previousPosition = position;
            previousTime = time;
            hasPrevious = true;
            return;
        }

        Vector3 displacement = position - previousPosition;
        displacement.y = 0f;

        DeltaTime = time - previousTime;
        Travelled = displacement.magnitude;
        previousPosition = position;
        previousTime = time;

        if (DeltaTime > Mathf.Epsilon &&
            windowed.TryAdd(Travelled, DeltaTime, Window, out float speed))
        {
            Speed = speed;
        }
    }
}
