using System;
using System.Net;
using System.Net.Sockets;
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

    protected override async Task<ConnectionResult> StartClientInternalAsync(ConnectionConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Address))
            return ConnectionResult.Fail("IP address is empty.");

        string ip = config.Address.Trim();

        if (!IsValidTargetIpAddress(ip))
            return ConnectionResult.Fail($"Invalid IP address: {ip}.");

        transport.SetConnectionData(
            ip,
            config.Port
        );

        return await StartClientAndWaitForConnectionAsync(
            ip,
            config.Port,
            config.ClientConnectionTimeoutSeconds
        );
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

    private async Task<ConnectionResult> StartClientAndWaitForConnectionAsync(
        string ip,
        ushort port,
        float timeoutSeconds)
    {
        TaskCompletionSource<ConnectionResult> completion = new TaskCompletionSource<ConnectionResult>();

        void HandleClientConnected(ulong clientId)
        {
            completion.TrySetResult(ConnectionResult.Ok($"LAN client connected to {ip}:{port}."));
        }

        void HandleClientDisconnected(ulong clientId)
        {
            completion.TrySetResult(ConnectionResult.Fail($"Failed to connect to {ip}:{port}."));
        }

        networkManager.OnClientConnectedCallback += HandleClientConnected;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;

        try
        {
            bool started = networkManager.StartClient();

            if (!started)
                return ConnectionResult.Fail("Failed to start LAN client.");

            double timeout = Math.Max(1f, timeoutSeconds);
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeout));
            Task completedTask = await Task.WhenAny(completion.Task, timeoutTask);

            if (completedTask == completion.Task)
                return await completion.Task;

            if (networkManager.IsListening)
                networkManager.Shutdown();

            return ConnectionResult.Fail($"Connection to {ip}:{port} timed out.");
        }
        finally
        {
            networkManager.OnClientConnectedCallback -= HandleClientConnected;
            networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }

    private static bool IsValidTargetIpAddress(string ip)
    {
        string[] parts = ip.Split('.');

        if (parts.Length != 4)
            return false;

        for (int i = 0; i < parts.Length; i++)
        {
            if (!byte.TryParse(parts[i], out _))
                return false;
        }

        if (!IPAddress.TryParse(ip, out IPAddress address))
            return false;

        return address.AddressFamily == AddressFamily.InterNetwork &&
               !IPAddress.Any.Equals(address) &&
               !IPAddress.Broadcast.Equals(address) &&
               !IPAddress.IPv6Any.Equals(address);
    }
}
