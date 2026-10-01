using System;
using Unity.Netcode;
using UnityEngine;

// One noise reaching the enemy's ears, as the clients see it.
//
// The id is what makes it an event rather than a value. Two identical noises
// in a row would leave the score unchanged, and a NetworkVariable that does not
// change tells nobody anything - so every report carries a fresh number and the
// clients react to the number moving, not to what it holds.
//
// It does not carry a position. Whoever plays a sound for this plays it on the
// enemy, because what is being presented is her noticing, not the noise.
public struct EnemyHeardNoiseSnapshot :
    INetworkSerializable,
    IEquatable<EnemyHeardNoiseSnapshot>
{
    public static readonly EnemyHeardNoiseSnapshot None =
        new(0u, 0f, GameplayNoiseSourceType.Unknown, 0d);

    private uint id;
    private float score;
    private GameplayNoiseSourceType source;
    private double heardAt;

    public uint Id => id;

    // In server time, so every machine measures the gap between two of them
    // alike - see the reaction's cooldown.
    public double HeardAt => heardAt;

    // Loudness after distance and age, so it is how loud the noise was to her
    // rather than how loud it was where it happened.
    public float Score => score;

    // What made it. Whether a kind is worth reacting to out loud is a
    // presentation question, answered where the clip lives.
    public GameplayNoiseSourceType Source => source;

    public bool HasReport => id != 0u;

    public EnemyHeardNoiseSnapshot(
        uint id,
        float score,
        GameplayNoiseSourceType source,
        double heardAt)
    {
        this.id = id;
        this.score = Mathf.Max(0f, score);
        this.source = source;
        this.heardAt = heardAt;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref id);
        serializer.SerializeValue(ref score);
        serializer.SerializeValue(ref source);
        serializer.SerializeValue(ref heardAt);
    }

    public bool Equals(EnemyHeardNoiseSnapshot other)
    {
        return id == other.id &&
               source == other.source &&
               Mathf.Approximately(score, other.score) &&
               heardAt.Equals(other.heardAt);
    }

    public override bool Equals(object obj)
    {
        return obj is EnemyHeardNoiseSnapshot other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(id, score, source, heardAt);
    }

    public static bool operator ==(
        EnemyHeardNoiseSnapshot left,
        EnemyHeardNoiseSnapshot right
    )
    {
        return left.Equals(right);
    }

    public static bool operator !=(
        EnemyHeardNoiseSnapshot left,
        EnemyHeardNoiseSnapshot right
    )
    {
        return !left.Equals(right);
    }
}
