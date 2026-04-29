using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public abstract class ConnectionProviderBase : IConnectionProvider
{
    public abstract ConnectionMode Mode { get; }

    protected readonly NetworkManager networkManager;
    protected readonly UnityTransport transport;

    protected ConnectionProviderBase(NetworkManager networkManager, UnityTransport transport)
    {
        this.networkManager = networkManager;
        this.transport = transport;
    }

    public Task<ConnectionResult> StartHostAsync(ConnectionRequest request)
    {
        ConnectionResult validationResult = Validate();

        if (!validationResult.Success) return Task.FromResult(validationResult);

        return StartHostInternalAsync(request);
    }

    public Task<ConnectionResult> StartClientAsync(ConnectionRequest request)
    {
        ConnectionResult validationResult = Validate();

        if (!validationResult.Success) return Task.FromResult(validationResult);

        return StartClientInternalAsync(request);
    }

    public Task<ConnectionResult> StartServerAsync(ConnectionRequest request)
    {
        ConnectionResult validationResult = Validate();

        if (!validationResult.Success) return Task.FromResult(validationResult);

        return StartServerInternalAsync(request);
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

    protected abstract Task<ConnectionResult> StartHostInternalAsync(ConnectionRequest request);
    protected abstract Task<ConnectionResult> StartClientInternalAsync(ConnectionRequest request);
    protected abstract Task<ConnectionResult> StartServerInternalAsync(ConnectionRequest request);
}
