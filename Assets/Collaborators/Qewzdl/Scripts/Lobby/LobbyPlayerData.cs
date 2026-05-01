using System;
using Unity.Collections;
using Unity.Netcode;

public struct LobbyPlayerData : INetworkSerializable, IEquatable<LobbyPlayerData>
{
    public ulong ClientId;
    public FixedString32Bytes PlayerName;
    public bool IsReady;
    public bool IsRoomOwner;
    public int CharacterId;

    public LobbyPlayerData(
        ulong clientId,
        string playerName,
        bool isReady,
        bool isRoomOwner,
        int characterId)
    {
        ClientId = clientId;
        PlayerName = playerName;
        IsReady = isReady;
        IsRoomOwner = isRoomOwner;
        CharacterId = characterId;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref PlayerName);
        serializer.SerializeValue(ref IsReady);
        serializer.SerializeValue(ref IsRoomOwner);
        serializer.SerializeValue(ref CharacterId);
    }

    public bool Equals(LobbyPlayerData other)
    {
        return ClientId == other.ClientId;
    }
}