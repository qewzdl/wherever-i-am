using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkConnectionService : MonoBehaviour
{
    public static NetworkConnectionService Instance { get; private set; }

    [Header("Network References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private UnityTransport transport;

    [Header("LAN Settings")]
    [SerializeField] private ushort port = 7777;
    [SerializeField] private string hostAddress = "127.0.0.1";
    [SerializeField] private string listenAddress = "0.0.0.0";

    private readonly Dictionary<ConnectionMode, IConnectionProvider> providers = new Dictionary<ConnectionMode, IConnectionProvider>();

    private IConnectionProvider activeProvider;

    public bool IsHost => networkManager != null && networkManager.IsHost;
    public bool IsClient => networkManager != null && networkManager.IsClient;
    public bool IsServer => networkManager != null && networkManager.IsServer;
    public bool IsConnected => networkManager != null && networkManager.IsConnectedClient;
    public bool IsListening => networkManager != null && networkManager.IsListening;

    private void Awake() 
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        InitializeReferences();
        InitializeProviders();
    }

    public Task<ConnectionResult> StartHostAsync()
    {
        ConnectionRequest request = new ConnectionRequest(
            ConnectionMode.LAN,
            ConnectionRole.Host,
            hostAddress,
            port,
            listenAddress
        );

        return StartConnectionAsync(request);
    }

    public Task<ConnectionResult> StartClientAsync(string ip)
    {
        ConnectionRequest request = new ConnectionRequest(
            ConnectionMode.LAN,
            ConnectionRole.Client,
            ip,
            port
        );

        return StartConnectionAsync(request);
    }

    public async Task<ConnectionResult> StartConnectionAsync(ConnectionRequest request)
    {
        ConnectionResult validationResult = CanStartConnection();

        if (!validationResult.Success)
        {
            Debug.LogError(validationResult.Message);
            return validationResult;
        }

        if (!providers.TryGetValue(request.Mode, out IConnectionProvider provider))
        {
            ConnectionResult result = ConnectionResult.Fail($"Connection provider not found for mode: {request.Mode}");
            Debug.LogError(result.Message);
            return result;
        }

        ConnectionResult connectionResult;

        switch (request.Role)
        {
            case ConnectionRole.Host:
                connectionResult = await provider.StartHostAsync(request);
                break;

            case ConnectionRole.Client:
                connectionResult = await provider.StartClientAsync(request);
                break;

            case ConnectionRole.Server:
                connectionResult = await provider.StartServerAsync(request);
                break;

            default:
                connectionResult = ConnectionResult.Fail("Unsupported connection role.");
                break;
        }

        if (connectionResult.Success)
        {
            activeProvider = provider;
            Debug.Log(connectionResult.Message);
        }
        else
        {
            Debug.LogError(connectionResult.Message);
        }

        return connectionResult;
    }

    public void Shutdown()
    {
        if (networkManager == null) return;

        if (!networkManager.IsListening &&
            !networkManager.IsClient &&
            !networkManager.IsServer)
        {
            return;
        }

        networkManager.Shutdown();
        activeProvider = null;

        Debug.Log("Network shutdown.");
    }

    private void InitializeReferences()
    {
        if (networkManager == null) networkManager = NetworkManager.Singleton;
        if (transport == null && networkManager != null) transport = networkManager.GetComponent<UnityTransport>();
    }

    private void InitializeProviders()
    {
        providers.Clear();

        IConnectionProvider lanProvider = new LANConnectionProvider(networkManager, transport);

        providers.Add(lanProvider.Mode, lanProvider);
    }

    private ConnectionResult CanStartConnection()
    {
        if (networkManager == null) return ConnectionResult.Fail("NetworkManager not found in the scene.");

        if (transport == null) return ConnectionResult.Fail("UnityTransport not found on NetworkManager.");

        if (networkManager.IsListening) return ConnectionResult.Fail("Network is already running.");

        return ConnectionResult.Ok("Connection can be started.");
    }
}
