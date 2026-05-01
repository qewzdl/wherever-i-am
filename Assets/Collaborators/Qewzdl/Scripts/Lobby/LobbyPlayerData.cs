using System;
using Unity.Collections;
using Unity.Netcode;

public struct LobbyPlayerData : INetworkSerializable, IEquatable<LobbyPlayerData>
{
    public ulong ClientId;
    public FixedString32Bytes PlayerName;
    public bool IsReady;
    public bool IsHost;
    public int CharacterId;

    public LobbyPlayerData(
        ulong clientId,
        string playerName,
        bool isReady,
        bool isHost,
        int characterId)
    {
        ClientId = clientId;
        PlayerName = playerName;
        IsReady = isReady;
        IsHost = isHost;
        CharacterId = characterId;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref PlayerName);
        serializer.SerializeValue(ref IsReady);
        serializer.SerializeValue(ref IsHost);
        serializer.SerializeValue(ref CharacterId);
    }

    public bool Equals(LobbyPlayerData other)
    {
        return ClientId == other.ClientId;
    }
}