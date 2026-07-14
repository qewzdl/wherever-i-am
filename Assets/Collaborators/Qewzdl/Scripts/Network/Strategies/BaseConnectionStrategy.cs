using System.Threading;
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

    public Task<ConnectionResult> StartHostAsync(
        ConnectionConfig config,
        CancellationToken cancellationToken)
    {
        ConnectionResult validationResult = Validate();

        if (!validationResult.Success) return Task.FromResult(validationResult);

        return StartHostInternalAsync(config, cancellationToken);
    }

    public Task<ConnectionResult> StartClientAsync(
        ConnectionConfig config,
        CancellationToken cancellationToken)
    {
        ConnectionResult validationResult = Validate();

        if (!validationResult.Success) return Task.FromResult(validationResult);

        return StartClientInternalAsync(config, cancellationToken);
    }

    public Task<ConnectionResult> StartServerAsync(
        ConnectionConfig config,
        CancellationToken cancellationToken)
    {
        ConnectionResult validationResult = Validate();

        if (!validationResult.Success) return Task.FromResult(validationResult);

        return StartServerInternalAsync(config, cancellationToken);
    }

    protected virtual ConnectionResult Validate()
    {
        if (networkManager == null)
        {
            return ConnectionResult.Fail(
                ConnectionErrorCode.NetworkManagerMissing,
                "Failed to start the network connection.",
                "NetworkManager is null in connection strategy.",
                false
            );
        }

        if (transport == null)
        {
            return ConnectionResult.Fail(
                ConnectionErrorCode.TransportMissing,
                "Failed to start the network connection.",
                "UnityTransport is null in connection strategy.",
                false
            );
        }

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

    protected Task<ConnectionResult> Fail(
        ConnectionErrorCode errorCode,
        string userMessage,
        string debugMessage = "",
        bool canRetry = true)
    {
        return Task.FromResult(ConnectionResult.Fail(
            errorCode,
            userMessage,
            debugMessage,
            canRetry
        ));
    }

    protected abstract Task<ConnectionResult> StartHostInternalAsync(
        ConnectionConfig config,
        CancellationToken cancellationToken);

    protected abstract Task<ConnectionResult> StartClientInternalAsync(
        ConnectionConfig config,
        CancellationToken cancellationToken);

    protected abstract Task<ConnectionResult> StartServerInternalAsync(
        ConnectionConfig config,
        CancellationToken cancellationToken);
}
