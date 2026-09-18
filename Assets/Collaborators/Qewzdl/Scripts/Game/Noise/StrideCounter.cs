// When a foot lands, measured in metres rather than seconds.
//
// A stride rather than a timer, so somebody who slows down takes fewer steps
// rather than the same number more quietly - which is what walking is. The
// side effect is in our favour: a creeping player is not only quieter but
// rarer, and a rare sound is harder to follow than a faint one.
//
// Pulled out of the emitter that uses it for the same reason the exertion
// model was: what surrounds it there is netcode and sampling, and this is the
// part that can be quietly wrong. It has been wrong once already - setting off
// used to be silent until a whole stride had gone by, which on top of the
// acceleration up to walking pace meant two steps of nothing every time
// anybody started moving, at the one moment a footstep is most expected.
public sealed class StrideCounter
{
    private float distanceSinceStep;
    private bool wasMoving;

    public void Reset()
    {
        distanceSinceStep = 0f;
        wasMoving = false;
    }

    // Call once per sample with how far the body travelled since the last one.
    // True means a foot lands now.
    public bool Advance(bool isMoving, float travelled, float strideLength)
    {
        if (!isMoving)
        {
            // Forgotten rather than kept. Creeping half a stride and then
            // standing up to walk would otherwise pay for that crouched half
            // with a step on the first loud frame - a footstep for ground
            // already crossed quietly.
            Reset();
            return false;
        }

        distanceSinceStep += travelled;

        // The first footfall of a movement lands when the movement starts. It
        // is the only step that does not wait for a stride, and shortening the
        // stride to make it arrive sooner is the wrong lever - that speeds up
        // every step instead.
        bool startedMoving = !wasMoving;
        wasMoving = true;

        if (startedMoving)
        {
            distanceSinceStep = 0f;
            return true;
        }

        if (distanceSinceStep < strideLength)
            return false;

        distanceSinceStep -= strideLength;
        return true;
    }
}
