using System;
using Unity.Collections;
using Unity.Netcode;

public struct ObjectiveNetworkState : INetworkSerializable, IEquatable<ObjectiveNetworkState>
{
    public FixedString64Bytes ObjectiveId;
    public int SequenceIndex;
    public ObjectiveRuntimeState State;
    public float Progress01;

    public static ObjectiveNetworkState None => new ObjectiveNetworkState
    {
        ObjectiveId = string.Empty,
        SequenceIndex = -1,
        State = ObjectiveRuntimeState.None,
        Progress01 = 0f
    };

    public bool HasObjective => !ObjectiveId.IsEmpty && SequenceIndex >= 0;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ObjectiveId);
        serializer.SerializeValue(ref SequenceIndex);
        serializer.SerializeValue(ref State);
        serializer.SerializeValue(ref Progress01);
    }

    public bool Equals(ObjectiveNetworkState other)
    {
        return ObjectiveId.Equals(other.ObjectiveId)
               && SequenceIndex == other.SequenceIndex
               && State == other.State
               && Progress01.Equals(other.Progress01);
    }
}