public class ConnectionConfig
{
    public ConnectionMode Mode { get; }
    public ConnectionRole Role { get; }

    public string Address { get; }
    public ushort Port { get; }
    public string ListenAddress { get; }
    public float ClientConnectionTimeoutSeconds { get; }

    public ConnectionConfig(
        ConnectionMode mode,
        ConnectionRole role,
        string address,
        ushort port,
        string listenAddress = "0.0.0.0",
        float clientConnectionTimeoutSeconds = 5f)
    {
        Mode = mode;
        Role = role;
        Address = address;
        Port = port;
        ListenAddress = listenAddress;
        ClientConnectionTimeoutSeconds = clientConnectionTimeoutSeconds;
    }
}
