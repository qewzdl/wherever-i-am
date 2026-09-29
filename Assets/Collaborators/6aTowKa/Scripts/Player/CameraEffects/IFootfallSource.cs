using System;

// Whatever decides when this player's feet land - the footsteps you hear - so
// the head bob you see can land on the same beat instead of keeping its own.
public interface IFootfallSource
{
    event Action Footfall;

    // Metres from one footfall to the next.
    float StrideLength { get; }
}
