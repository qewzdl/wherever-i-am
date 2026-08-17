using UnityEngine;

[CreateAssetMenu(menuName = "Wherever I Am/Network/Connection Config", fileName = "NetworkConnectionConfig")]
public sealed class NetworkConnectionConfig : ScriptableObject
{
    [Header("Compatibility")]
    [Min(1)]
    [Tooltip("Increment when network prefabs, RPC contracts, or in-scene NetworkObjects become incompatible with previous builds.")]
    [SerializeField] private ushort protocolVersion = 2;

    [Header("LAN")]
    [SerializeField] private string hostAddress;
    [SerializeField] private ushort port;
    [SerializeField] private string listenAddress;
    [SerializeField] private float clientConnectionTimeoutSeconds;

    [Header("Messages")]
    [SerializeField] private string hostClosedSessionReason =
        "The host closed the lobby.";

    public ushort ProtocolVersion => protocolVersion;
    public string HostAddress => hostAddress;
    public ushort Port => port;
    public string ListenAddress => listenAddress;
    public string HostClosedSessionReason => hostClosedSessionReason;
    public float ClientConnectionTimeoutSeconds => clientConnectionTimeoutSeconds;

    public bool Validate(Object context)
    {
        bool valid = true;

        if (protocolVersion == 0)
        {
            Debug.LogError($"{nameof(NetworkConnectionConfig)} has invalid protocol version.", context);
            valid = false;
        }

        if (string.IsNullOrWhiteSpace(hostAddress))
        {
            Debug.LogError($"{nameof(NetworkConnectionConfig)} is missing host address.", context);
            valid = false;
        }

        if (port == 0)
        {
            Debug.LogError($"{nameof(NetworkConnectionConfig)} has invalid port.", context);
            valid = false;
        }

        if (string.IsNullOrWhiteSpace(listenAddress))
        {
            Debug.LogError($"{nameof(NetworkConnectionConfig)} is missing listen address.", context);
            valid = false;
        }

        if (clientConnectionTimeoutSeconds <= 0f)
        {
            Debug.LogError($"{nameof(NetworkConnectionConfig)} has invalid client connection timeout.", context);
            valid = false;
        }

        return valid;
    }
}
