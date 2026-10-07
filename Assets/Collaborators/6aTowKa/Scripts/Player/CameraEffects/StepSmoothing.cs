using UnityEngine;

// Where the eyes still are after the body was put up a step: as high as they
// were, closing on the body over a fraction of a second.
//
// The body goes up at once - the physics needs it standing on the step, not
// halfway into it - and the view follows, so stepping up reads as a step and
// not a hop. Only what the body is seen to rise by is taken back, and only as
// much as it was put up: the body interpolates, so the rise shows over the
// frame or two after the step rather than all in the frame of it, and taking
// it all back at once dropped the view below where it was. A rise with no
// step behind it - a jump, a ramp - is left alone.
//
// Applied to the camera on its own rather than as one of the camera effects:
// those can be turned down in the settings, and this one only takes a jolt
// away.
public sealed class StepSmoothing
{
    // A step the body is not seen to rise by within this long never showed.
    private const float PendingSeconds = 0.1f;

    private readonly float smoothTime;
    private float velocity;
    private float pending;
    private float pendingAge;
    private float lastHeight;
    private bool hasLastHeight;

    // How long the view takes to close on the body, roughly: it is within a
    // twentieth of it after about two and a half times this. Eased in and
    // out - it closed fastest at the very start, and that was a jolt of its
    // own.
    public StepSmoothing(float smoothTime = 0.12f)
    {
        this.smoothTime = Mathf.Max(0.01f, smoothTime);
    }

    // World up; added to the camera's height.
    public float Offset { get; private set; }

    public void SteppedUp(float rise)
    {
        pending += Mathf.Max(0f, rise);
        pendingAge = 0f;
    }

    // Once a frame, with the height the body is drawn at.
    public void Follow(float bodyHeight, float deltaTime)
    {
        deltaTime = Mathf.Max(0f, deltaTime);
        float risen = hasLastHeight ? bodyHeight - lastHeight : 0f;
        lastHeight = bodyHeight;
        hasLastHeight = true;

        if (deltaTime > 0f)
            Offset = Mathf.SmoothDamp(Offset, 0f, ref velocity, smoothTime, Mathf.Infinity, deltaTime);

        if (pending > 0f && risen > 0f)
        {
            float taken = Mathf.Min(risen, pending);
            Offset -= taken;
            pending -= taken;
        }

        pendingAge += deltaTime;

        if (pendingAge > PendingSeconds)
            pending = 0f;

        if (Mathf.Abs(Offset) < 0.0005f)
        {
            Offset = 0f;
            velocity = 0f;
        }
    }

    public void Reset()
    {
        Offset = 0f;
        velocity = 0f;
        pending = 0f;
        hasLastHeight = false;
    }
}
