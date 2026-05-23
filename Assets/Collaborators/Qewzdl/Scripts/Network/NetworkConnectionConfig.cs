using UnityEngine;

[CreateAssetMenu(menuName = "Wherever I Am/Network/Connection Config", fileName = "NetworkConnectionConfig")]
public sealed class NetworkConnectionConfig : ScriptableObject
{
    [Header("LAN")]
    [SerializeField] private string hostAddress;
    [SerializeField] private ushort port;
    [SerializeField] private string listenAddress;
    [SerializeField] private float clientConnectionTimeoutSeconds;

    public string HostAddress => hostAddress;
    public ushort Port => port;
    public string ListenAddress => listenAddress;
    public float ClientConnectionTimeoutSeconds => clientConnectionTimeoutSeconds;

    public bool Validate(Object context)
    {
        bool valid = true;

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