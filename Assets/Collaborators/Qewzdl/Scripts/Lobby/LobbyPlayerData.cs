using System;
using Unity.Collections;
using Unity.Netcode;

public struct LobbyPlayerData : INetworkSerializable, IEquatable<LobbyPlayerData>
{
    public ulong ClientId;
    public FixedString32Bytes PlayerName;
    public bool IsReady;

    public LobbyPlayerData(
        ulong clientId,
        string playerName,
        bool isReady)
    {
        ClientId = clientId;
        PlayerName = playerName;
        IsReady = isReady;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref PlayerName);
        serializer.SerializeValue(ref IsReady);
    }

    public bool Equals(LobbyPlayerData other)
    {
        return ClientId == other.ClientId &&
               PlayerName.Equals(other.PlayerName) &&
               IsReady == other.IsReady;
    }

    public override bool Equals(object obj)
    {
        return obj is LobbyPlayerData other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hashCode = ClientId.GetHashCode();
            hashCode = (hashCode * 397) ^ PlayerName.GetHashCode();
            hashCode = (hashCode * 397) ^ IsReady.GetHashCode();
            return hashCode;
        }
    }
}
