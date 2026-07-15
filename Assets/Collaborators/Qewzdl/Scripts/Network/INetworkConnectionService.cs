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

    Task<ConnectionResult> StartHostAsync();
    Task<ConnectionResult> StartClientAsync(string ip);
    Task<ConnectionResult> StartConnectionAsync(ConnectionConfig config);
    Task ShutdownAndWaitAsync(NetworkShutdownMode mode = NetworkShutdownMode.Graceful);
}
