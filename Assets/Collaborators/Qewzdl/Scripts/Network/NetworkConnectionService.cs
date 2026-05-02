using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkConnectionService : MonoBehaviour
{
    [Header("Network References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private UnityTransport transport;

    [Header("LAN Settings")]
    [SerializeField] private ushort port = 7777;
    [SerializeField] private string hostAddress = "127.0.0.1";
    [SerializeField] private string listenAddress = "0.0.0.0";

    private readonly Dictionary<ConnectionMode, IConnectionStrategy> strategies = new Dictionary<ConnectionMode, IConnectionStrategy>();

    private IConnectionStrategy activeStrategy;

    public bool IsHost => networkManager != null && networkManager.IsHost;
    public bool IsClient => networkManager != null && networkManager.IsClient;
    public bool IsServer => networkManager != null && networkManager.IsServer;
    public bool IsConnected => networkManager != null && networkManager.IsConnectedClient;
    public bool IsListening => networkManager != null && networkManager.IsListening;

    private void Awake()
    {
        ResolveReferences();
        InitializeStrategies();
    }

    public Task<ConnectionResult> StartHostAsync()
    {
        ConnectionConfig config = new ConnectionConfig(
            ConnectionMode.Lan,
            ConnectionRole.Host,
            hostAddress,
            port,
            listenAddress
        );

        return StartConnectionAsync(config);
    }

    public Task<ConnectionResult> StartClientAsync(string ip)
    {
        ConnectionConfig config = new ConnectionConfig(
            ConnectionMode.Lan,
            ConnectionRole.Client,
            ip,
            port
        );

        return StartConnectionAsync(config);
    }

    public async Task<ConnectionResult> StartConnectionAsync(ConnectionConfig config)
    {
        ConnectionResult validationResult = CanStartConnection();

        if (!validationResult.Success)
        {
            Debug.LogError(validationResult.Message);
            return validationResult;
        }

        if (!strategies.TryGetValue(config.Mode, out IConnectionStrategy strategy))
        {
            ConnectionResult result = ConnectionResult.Fail($"Connection strategy not found for mode: {config.Mode}");
            Debug.LogError(result.Message);
            return result;
        }

        ConnectionResult connectionResult;

        switch (config.Role)
        {
            case ConnectionRole.Host:
                connectionResult = await strategy.StartHostAsync(config);
                break;

            case ConnectionRole.Client:
                connectionResult = await strategy.StartClientAsync(config);
                break;

            case ConnectionRole.Server:
                connectionResult = await strategy.StartServerAsync(config);
                break;

            default:
                connectionResult = ConnectionResult.Fail("Unsupported connection role.");
                break;
        }

        if (connectionResult.Success)
        {
            activeStrategy = strategy;
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
        if (activeStrategy != null)
        {
            activeStrategy.Shutdown();
            activeStrategy = null;

            Debug.Log("Network shutdown by active strategy.");
            return;
        }

        if (networkManager == null)
            return;

        if (!networkManager.IsListening &&
            !networkManager.IsClient &&
            !networkManager.IsServer)
        {
            return;
        }

        networkManager.Shutdown();

        Debug.Log("Network shutdown.");
    }

    private void ResolveReferences()
    {
        if (networkManager == null)
            networkManager = GetComponent<NetworkManager>();

        if (networkManager == null)
            networkManager = NetworkManager.Singleton;

        if (transport == null && networkManager != null)
            transport = networkManager.GetComponent<UnityTransport>();
    }

    private void InitializeStrategies()
    {
        strategies.Clear();

        IConnectionStrategy lanStrategy = new LanConnectionStrategy(networkManager, transport);

        strategies.Add(lanStrategy.Mode, lanStrategy);
    }

    private ConnectionResult CanStartConnection()
    {
        if (networkManager == null)
            return ConnectionResult.Fail("NetworkManager not found in the scene.");

        if (transport == null)
            return ConnectionResult.Fail("UnityTransport not found on NetworkManager.");

        if (networkManager.IsListening)
            return ConnectionResult.Fail("Network is already running.");

        return ConnectionResult.Ok("Connection can be started.");
    }
}