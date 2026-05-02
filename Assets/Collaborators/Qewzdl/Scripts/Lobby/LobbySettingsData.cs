using System;
using Unity.Netcode;

public struct LobbySettingsData : INetworkSerializable, IEquatable<LobbySettingsData>
{
    public int MinPlayersToStart;
    public int MaxPlayers;
    public bool RequireAllPlayersReady;

    public int GameModeId;
    public int MapId;

    public LobbySettingsData(
        int minPlayersToStart,
        int maxPlayers,
        bool requireAllPlayersReady,
        int gameModeId,
        int mapId)
    {
        MinPlayersToStart = minPlayersToStart;
        MaxPlayers = maxPlayers;
        RequireAllPlayersReady = requireAllPlayersReady;
        GameModeId = gameModeId;
        MapId = mapId;
    }

    public static LobbySettingsData CreateDefault()
    {
        return new LobbySettingsData(
            minPlayersToStart: 1,
            maxPlayers: 4,
            requireAllPlayersReady: true,
            gameModeId: 0,
            mapId: 0
        );
    }

    public static LobbySettingsData FromConfig(LobbyConfig config)
    {
        if (config == null)
            return CreateDefault();

        return new LobbySettingsData(
            config.MinPlayersToStart,
            config.MaxPlayers,
            config.RequireAllPlayersReady,
            config.DefaultGameModeId,
            config.DefaultMapId
        );
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref MinPlayersToStart);
        serializer.SerializeValue(ref MaxPlayers);
        serializer.SerializeValue(ref RequireAllPlayersReady);
        serializer.SerializeValue(ref GameModeId);
        serializer.SerializeValue(ref MapId);
    }

    public bool Equals(LobbySettingsData other)
    {
        return MinPlayersToStart == other.MinPlayersToStart &&
               MaxPlayers == other.MaxPlayers &&
               RequireAllPlayersReady == other.RequireAllPlayersReady &&
               GameModeId == other.GameModeId &&
               MapId == other.MapId;
    }
}
