using System;
using Unity.Netcode;

// The enemy's state, together with which entry into it this is and when, in
// server time, it began.
//
// The state alone said what she was doing. Her sounds need the other two:
// the number is what every machine rolls her sounds from (SoundRoll), and the
// moment is what every machine times her repeated sounds from, so that two
// players in one room hear her breathe at once rather than each on a clock of
// their own.
public struct EnemyStateEntry :
    INetworkSerializable,
    IEquatable<EnemyStateEntry>
{
    // The state she was spawned in, which nothing numbered.
    public static readonly EnemyStateEntry Spawned = new(EnemyState.Idle, 0u, 0d);

    private EnemyState state;
    private uint number;
    private double enteredAt;

    public EnemyState State => state;
    public uint Number => number;
    public double EnteredAt => enteredAt;
    public bool IsNumbered => number != 0u;

    public EnemyStateEntry(EnemyState state, uint number, double enteredAt)
    {
        this.state = state;
        this.number = number;
        this.enteredAt = enteredAt;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref state);
        serializer.SerializeValue(ref number);
        serializer.SerializeValue(ref enteredAt);
    }

    public bool Equals(EnemyStateEntry other)
    {
        return state == other.state &&
               number == other.number &&
               enteredAt.Equals(other.enteredAt);
    }

    public override bool Equals(object obj)
    {
        return obj is EnemyStateEntry other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(state, number, enteredAt);
    }
}
