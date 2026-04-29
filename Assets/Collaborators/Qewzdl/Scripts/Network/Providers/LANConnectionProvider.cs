using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class LANConnectionProvider : ConnectionProviderBase
{
    public override ConnectionMode Mode => ConnectionMode.LAN;

    public LANConnectionProvider(NetworkManager networkManager, UnityTransport transport) : base(networkManager, transport) {}

    protected override Task<ConnectionResult> StartHostInternalAsync(ConnectionRequest request)
    {
        transport.SetConnectionData(
            request.Address,
            request.Port,
            request.ListenAddress
        );

        bool started = networkManager.StartHost();

        return started ? Success("LAN host started.") : Fail("Failed to start LAN host.");
    }

    protected override Task<ConnectionResult> StartClientInternalAsync(ConnectionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Address)) return Fail("IP address is empty.");

        string ip = request.Address.Trim();

        transport.SetConnectionData(
            ip,
            request.Port
        );

        bool started = networkManager.StartClient();

        return started ? Success($"LAN client connecting to {ip}:{request.Port}.") : Fail("Failed to start LAN client.");
    }

    protected override Task<ConnectionResult> StartServerInternalAsync(ConnectionRequest request)
    {
        transport.SetConnectionData(
            request.Address,
            request.Port,
            request.ListenAddress
        );
        
        bool started = networkManager.StartServer();

        return started ? Success("LAN server started.") : Fail("Failed to start LAN server.");
    }
}
