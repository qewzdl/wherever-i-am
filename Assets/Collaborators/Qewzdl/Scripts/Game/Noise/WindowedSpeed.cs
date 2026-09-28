// How fast a watched body is going, judged over a short stretch of time
// rather than one frame.
//
// Somebody else's player does not move every frame: their position arrives in
// network ticks, and between two ticks - or once interpolation has run out of
// buffer - it stands still. Judged a frame at a time, that player stopped and
// set off again on every tick, and setting off lands a foot at once, so their
// footsteps came one per tick with no gap between them. The enemy, listening
// on the server to clients' players, heard the same.
public sealed class WindowedSpeed
{
    private float travelled;
    private float elapsed;

    public void Reset()
    {
        travelled = 0f;
        elapsed = 0f;
    }

    // Call once per frame with how far the body went since the last one.
    // True once a whole window has gone by, with the average speed over it.
    public bool TryAdd(float distance, float deltaTime, float window, out float speed)
    {
        travelled += distance;
        elapsed += deltaTime;
        speed = 0f;

        if (elapsed < window || elapsed <= 0f)
            return false;

        speed = travelled / elapsed;
        Reset();
        return true;
    }
}
