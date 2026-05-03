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

        return started
            ? Success("LAN host started.")
            : Fail(
                ConnectionErrorCode.ConnectionFailed,
                "Failed to create LAN lobby.",
                "NetworkManager.StartHost returned false.",
                true
            );
    }

    protected override async Task<ConnectionResult> StartClientInternalAsync(ConnectionConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Address))
        {
            return ConnectionResult.Fail(
                ConnectionErrorCode.EmptyIpAddress,
                "Enter the host IP address.",
                "LAN connection failed: IP address is empty.",
                true
            );
        }

        string ip = config.Address.Trim();

        if (!IsValidTargetIpAddress(ip))
        {
            return ConnectionResult.Fail(
                ConnectionErrorCode.InvalidIpAddress,
                "The IP address is invalid.",
                $"Invalid LAN target IP address: {ip}.",
                true
            );
        }

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

        return started
            ? Success("LAN server started.")
            : Fail(
                ConnectionErrorCode.ConnectionFailed,
                "Failed to start LAN server.",
                "NetworkManager.StartServer returned false.",
                true
            );
    }

    private async Task<ConnectionResult> StartClientAndWaitForConnectionAsync(
        string ip,
        ushort port,
        float timeoutSeconds)
    {
        TaskCompletionSource<ConnectionResult> completion = new TaskCompletionSource<ConnectionResult>();

        void HandleClientConnected(ulong clientId)
        {
            if (clientId != networkManager.LocalClientId)
                return;

            completion.TrySetResult(ConnectionResult.Ok(
                $"LAN client connected to {ip}:{port}."
            ));
        }

        void HandleClientDisconnected(ulong clientId)
        {
            if (clientId != networkManager.LocalClientId)
                return;

            string disconnectReason = GetServerDisconnectReason();
            string userMessage = string.IsNullOrWhiteSpace(disconnectReason)
                ? "Failed to connect to the host. Check the IP address and make sure the lobby is already running."
                : disconnectReason;

            completion.TrySetResult(ConnectionResult.Fail(
                ConnectionErrorCode.ConnectionFailed,
                userMessage,
                $"LAN client disconnected while trying to connect to {ip}:{port}. {disconnectReason}",
                true
            ));
        }

        networkManager.OnClientConnectedCallback += HandleClientConnected;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;

        try
        {
            bool started = networkManager.StartClient();

            if (!started)
            {
                return ConnectionResult.Fail(
                    ConnectionErrorCode.ConnectionFailed,
                    "Failed to start the connection.",
                    $"NetworkManager.StartClient returned false for LAN target {ip}:{port}.",
                    true
                );
            }

            double timeout = Math.Max(1f, timeoutSeconds);
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeout));
            Task completedTask = await Task.WhenAny(completion.Task, timeoutTask);

            if (completedTask == completion.Task)
                return await completion.Task;

            if (networkManager.IsListening)
                networkManager.Shutdown();

            return ConnectionResult.Fail(
                ConnectionErrorCode.ConnectionTimeout,
                "Connection timed out. Check the IP address and make sure the host has already created a lobby.",
                $"Connection to {ip}:{port} timed out after {timeout} seconds.",
                true
            );
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

    private string GetServerDisconnectReason()
    {
        if (networkManager == null)
            return string.Empty;

        string reason = networkManager.DisconnectReason;

        if (string.IsNullOrWhiteSpace(reason) || reason.StartsWith("[Disconnect Event]", StringComparison.Ordinal))
            return string.Empty;

        return reason;
    }
}
