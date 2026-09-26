using System.Threading.Tasks;

internal interface INetworkConnectionService
{
    bool IsHost { get; }
    bool IsClient { get; }
    bool IsServer { get; }
    bool IsConnected { get; }
    bool IsListening { get; }
    bool IsRunning { get; }
    bool IsConnectionReady { get; }

    // localOnly: listen on this machine alone, on whatever port is free - a
    // singleplayer game, which nobody else should be able to reach and which
    // must not clash with a lobby another copy of the game is hosting.
    Task<ConnectionResult> StartHostAsync(bool localOnly = false);
    Task<ConnectionResult> StartClientAsync(string ip);
    Task<ConnectionResult> StartConnectionAsync(ConnectionConfig config);
    Task ShutdownAndWaitAsync(NetworkShutdownMode mode = NetworkShutdownMode.Graceful);
}
