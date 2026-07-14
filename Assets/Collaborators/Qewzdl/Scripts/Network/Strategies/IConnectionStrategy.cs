using System.Threading;
using System.Threading.Tasks;

public interface IConnectionStrategy
{
    ConnectionMode Mode { get; }

    Task<ConnectionResult> StartHostAsync(ConnectionConfig config, CancellationToken cancellationToken);
    Task<ConnectionResult> StartClientAsync(ConnectionConfig config, CancellationToken cancellationToken);
    Task<ConnectionResult> StartServerAsync(ConnectionConfig config, CancellationToken cancellationToken);
}
