using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class LanConnectionStrategy : BaseConnectionStrategy
{
    public override ConnectionMode Mode => ConnectionMode.Lan;

    public LanConnectionStrategy(NetworkManager networkManager, UnityTransport transport) : base(networkManager, transport) {}

    protected override Task<ConnectionResult> StartHostInternalAsync(ConnectionConfig config)
    {
        transport.SetConnectionData(
            config.Address,
            config.Port,
            config.ListenAddress
        );

        bool started = networkManager.StartHost();

        return started ? Success("LAN host started.") : Fail("Failed to start LAN host.");
    }

    protected override Task<ConnectionResult> StartClientInternalAsync(ConnectionConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Address)) return Fail("IP address is empty.");

        string ip = config.Address.Trim();

        transport.SetConnectionData(
            ip,
            config.Port
        );

        bool started = networkManager.StartClient();

        return started ? Success($"LAN client connecting to {ip}:{config.Port}.") : Fail("Failed to start LAN client.");
    }

    protected override Task<ConnectionResult> StartServerInternalAsync(ConnectionConfig config)
    {
        transport.SetConnectionData(
            config.Address,
            config.Port,
            config.ListenAddress
        );
        
        bool started = networkManager.StartServer();

        return started ? Success("LAN server started.") : Fail("Failed to start LAN server.");
    }
}
