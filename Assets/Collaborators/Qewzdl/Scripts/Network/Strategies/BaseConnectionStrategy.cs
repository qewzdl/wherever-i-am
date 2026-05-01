using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public abstract class BaseConnectionStrategy : IConnectionStrategy
{
    public abstract ConnectionMode Mode { get; }

    protected readonly NetworkManager networkManager;
    protected readonly UnityTransport transport;

    protected BaseConnectionStrategy(NetworkManager networkManager, UnityTransport transport)
    {
        this.networkManager = networkManager;
        this.transport = transport;
    }

    public Task<ConnectionResult> StartHostAsync(ConnectionConfig config)
    {
        ConnectionResult validationResult = Validate();

        if (!validationResult.Success) return Task.FromResult(validationResult);

        return StartHostInternalAsync(config);
    }

    public Task<ConnectionResult> StartClientAsync(ConnectionConfig config)
    {
        ConnectionResult validationResult = Validate();

        if (!validationResult.Success) return Task.FromResult(validationResult);

        return StartClientInternalAsync(config);
    }

    public Task<ConnectionResult> StartServerAsync(ConnectionConfig config)
    {
        ConnectionResult validationResult = Validate();

        if (!validationResult.Success) return Task.FromResult(validationResult);

        return StartServerInternalAsync(config);
    }

    public virtual void Shutdown()
    {
        if (networkManager != null && networkManager.IsListening)
        {
            networkManager.Shutdown();
        }
    }

    protected virtual ConnectionResult Validate()
    {
        if (networkManager == null) return ConnectionResult.Fail("NetworkManager is null.");

        if (transport == null) return ConnectionResult.Fail("UnityComponent is null.");

        return ConnectionResult.Ok("Network setup is valid.");
    }

    protected Task<ConnectionResult> Success(string message)
    {
        return Task.FromResult(ConnectionResult.Ok(message));
    }

    protected Task<ConnectionResult> Fail(string message)
    {
        return Task.FromResult(ConnectionResult.Fail(message));
    }

    protected abstract Task<ConnectionResult> StartHostInternalAsync(ConnectionConfig config);
    protected abstract Task<ConnectionResult> StartClientInternalAsync(ConnectionConfig config);
    protected abstract Task<ConnectionResult> StartServerInternalAsync(ConnectionConfig config);
}
