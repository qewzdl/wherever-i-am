using System.Threading.Tasks;

public interface IConnectionProvider
{
    ConnectionMode Mode { get; }

    Task<ConnectionResult> StartHostAsync(ConnectionRequest request);
    Task<ConnectionResult> StartClientAsync(ConnectionRequest request);
    Task<ConnectionResult> StartServerAsync(ConnectionRequest request);

    void Shutdown();
}
