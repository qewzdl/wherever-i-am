using System.Threading.Tasks;

public interface IConnectionStrategy
{
    ConnectionMode Mode { get; }

    Task<ConnectionResult> StartHostAsync(ConnectionConfig config);
    Task<ConnectionResult> StartClientAsync(ConnectionConfig config);
    Task<ConnectionResult> StartServerAsync(ConnectionConfig config);

    void Shutdown();
}
