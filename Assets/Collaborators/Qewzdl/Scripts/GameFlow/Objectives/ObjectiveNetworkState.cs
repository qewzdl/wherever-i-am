using System;
using Unity.Collections;
using Unity.Netcode;

// The sequence asset is the same on every peer, so the index says which
// objective this is. A name went over the wire beside it until it became clear
// it was answering a question the index had already answered.
public struct ObjectiveNetworkState : INetworkSerializable, IEquatable<ObjectiveNetworkState>
{
    public int SequenceIndex;
    public ObjectiveRuntimeState State;
    public float Progress01;

    public static ObjectiveNetworkState None => new ObjectiveNetworkState
    {
        SequenceIndex = -1,
        State = ObjectiveRuntimeState.None,
        Progress01 = 0f
    };

    public bool HasObjective => SequenceIndex >= 0;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref SequenceIndex);
        serializer.SerializeValue(ref State);
        serializer.SerializeValue(ref Progress01);
    }

    public bool Equals(ObjectiveNetworkState other)
    {
        return SequenceIndex == other.SequenceIndex
               && State == other.State
               && Progress01.Equals(other.Progress01);
    }
}