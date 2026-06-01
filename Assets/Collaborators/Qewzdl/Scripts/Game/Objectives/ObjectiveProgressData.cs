using System;
using Unity.Collections;
using Unity.Netcode;

public struct ObjectiveProgressData : INetworkSerializable, IEquatable<ObjectiveProgressData>
{
    public FixedString64Bytes ObjectiveId;
    public FixedString128Bytes DisplayName;
    public int CurrentValue;
    public int TargetValue;
    public bool IsCompleted;
    public ObjectiveState State;

    public static ObjectiveProgressData Create(
        string objectiveId,
        string displayName,
        int currentValue,
        int targetValue,
        bool isCompleted,
        ObjectiveState state)
    {
        return new ObjectiveProgressData
        {
            ObjectiveId = objectiveId ?? string.Empty,
            DisplayName = displayName ?? string.Empty,
            CurrentValue = currentValue,
            TargetValue = targetValue,
            IsCompleted = isCompleted,
            State = state
        };
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ObjectiveId);
        serializer.SerializeValue(ref DisplayName);
        serializer.SerializeValue(ref CurrentValue);
        serializer.SerializeValue(ref TargetValue);
        serializer.SerializeValue(ref IsCompleted);
        serializer.SerializeValue(ref State);
    }

    public bool Equals(ObjectiveProgressData other)
    {
        return ObjectiveId.Equals(other.ObjectiveId)
               && DisplayName.Equals(other.DisplayName)
               && CurrentValue == other.CurrentValue
               && TargetValue == other.TargetValue
               && IsCompleted == other.IsCompleted
               && State == other.State;
    }
}