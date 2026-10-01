// A throw of the dice for one sound that every machine makes the same.
//
// Each machine used to roll its own: which take of a growl, how high, whether
// it came at all, and when the next one would. Two players in one room heard
// two different enemies - one breathing now, the other in three seconds - and
// in a game where a sound is where she is, that is two different answers to
// the only question that matters.
//
// So the roll comes from what every machine already agrees on: whose sound it
// is (a network object id) and which occasion (a numbered event, the n-th
// repeat). Same inputs, same hash, same sound, with nothing sent for it.
public readonly struct SoundRoll
{
    private readonly uint seed;

    private SoundRoll(uint seed)
    {
        this.seed = seed;
    }

    public static SoundRoll For(ulong owner, uint occasion, uint slot = 0u, uint occurrence = 0u)
    {
        uint seed = Mix((uint)owner ^ Mix((uint)(owner >> 32)));
        seed = Mix(seed ^ occasion);
        seed = Mix(seed ^ (slot * 0x9E3779B9u));
        seed = Mix(seed ^ (occurrence * 0x85EBCA6Bu));
        return new SoundRoll(seed);
    }

    // For a sound only one machine plays, where agreeing is beside the point.
    public static SoundRoll Unshared()
    {
        return new SoundRoll((uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue));
    }

    // In [0, 1), a different number for each thing being decided.
    public float this[SoundDraw draw] =>
        (Mix(seed ^ ((uint)draw + 1u) * 0xC2B2AE35u) >> 8) / 16777216f;

    private static uint Mix(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }
}

public enum SoundDraw
{
    Chance,
    Clip,
    Volume,
    Pitch,
    Delay
}
