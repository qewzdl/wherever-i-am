using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class NetworkConnectionShutdownPlayModeTests
{
    private const float OperationTimeoutSeconds = 10f;

    private readonly List<NetworkEndpointFixture> endpoints = new();

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        for (int i = 0; i < endpoints.Count; i++)
        {
            NetworkManager manager = endpoints[i].Manager;

            if (manager != null && !IsFullyStopped(manager))
                manager.Shutdown(discardMessageQueue: true);
        }

        float timeoutAt = Time.realtimeSinceStartup + OperationTimeoutSeconds;

        while (!AreAllEndpointsStopped() && Time.realtimeSinceStartup < timeoutAt)
            yield return null;

        for (int i = endpoints.Count - 1; i >= 0; i--)
            endpoints[i].Dispose();

        endpoints.Clear();
        yield return null;
    }

    [UnityTest]
    public IEnumerator HostShutdown_WaitsForClientAndServerStoppedCallbacks()
    {
        NetworkEndpointFixture host = CreateEndpoint("Shutdown host");
        Assert.That(host.Manager.StartHost(), Is.True);
        yield return WaitForCondition(
            () => host.Manager.IsHost && host.Manager.IsListening,
            "Host did not start.");

        int clientStoppedCount = 0;
        int serverStoppedCount = 0;
        bool clientReportedHost = false;
        bool serverReportedHost = false;

        host.Manager.OnClientStopped += wasHost =>
        {
            clientStoppedCount++;
            clientReportedHost = wasHost;
        };
        host.Manager.OnServerStopped += wasHost =>
        {
            serverStoppedCount++;
            serverReportedHost = wasHost;
        };

        Task shutdown = host.ConnectionService.ShutdownAndWaitAsync();
        yield return WaitForTask(shutdown, "Host shutdown did not complete.");

        Assert.That(IsFullyStopped(host.Manager), Is.True);
        Assert.That(clientStoppedCount, Is.EqualTo(1));
        Assert.That(serverStoppedCount, Is.EqualTo(1));
        Assert.That(clientReportedHost, Is.True);
        Assert.That(serverReportedHost, Is.True);
    }

    [UnityTest]
    public IEnumerator ClientShutdown_WaitsForClientStoppedCallback()
    {
        NetworkEndpointFixture server = CreateEndpoint("Client shutdown server", false);
        Assert.That(server.Manager.StartHost(), Is.True);
        yield return WaitForCondition(
            () => server.Manager.IsHost && server.Manager.IsListening &&
                  server.Transport.GetLocalEndpoint().Port != 0,
            "Client test server did not start.");

        ushort serverPort = server.Transport.GetLocalEndpoint().Port;
        NetworkEndpointFixture client = CreateEndpoint("Shutdown client");
        client.Transport.SetConnectionData("127.0.0.1", serverPort);

        Assert.That(client.Manager.StartClient(), Is.True);
        yield return WaitForCondition(
            () => client.Manager.IsConnectedClient &&
                  server.Manager.ConnectedClientsIds.Count == 2,
            "Client did not connect to the test host.");

        int clientStoppedCount = 0;
        int serverStoppedCount = 0;
        bool clientReportedHost = true;

        client.Manager.OnClientStopped += wasHost =>
        {
            clientStoppedCount++;
            clientReportedHost = wasHost;
        };
        client.Manager.OnServerStopped += _ => serverStoppedCount++;

        Task shutdown = client.ConnectionService.ShutdownAndWaitAsync();
        yield return WaitForTask(shutdown, "Client shutdown did not complete.");

        Assert.That(IsFullyStopped(client.Manager), Is.True);
        Assert.That(clientStoppedCount, Is.EqualTo(1));
        Assert.That(serverStoppedCount, Is.Zero);
        Assert.That(clientReportedHost, Is.False);
        Assert.That(server.Manager.IsListening, Is.True);
    }

    [UnityTest]
    public IEnumerator RepeatedImmediateShutdown_EscalatesSharedTaskAndStopsOnce()
    {
        NetworkEndpointFixture host = CreateEndpoint("Repeated immediate host");
        Assert.That(host.Manager.StartHost(), Is.True);
        yield return WaitForCondition(
            () => host.Manager.IsHost && host.Manager.IsListening,
            "Host did not start.");

        int clientStoppedCount = 0;
        int serverStoppedCount = 0;
        host.Manager.OnClientStopped += _ => clientStoppedCount++;
        host.Manager.OnServerStopped += _ => serverStoppedCount++;

        Task graceful = host.ConnectionService.ShutdownAndWaitAsync(
            NetworkShutdownMode.Graceful);
        Task immediate = host.ConnectionService.ShutdownAndWaitAsync(
            NetworkShutdownMode.Immediate);
        Task repeated = host.ConnectionService.ShutdownAndWaitAsync(
            NetworkShutdownMode.Immediate);

        Assert.That(immediate, Is.SameAs(graceful));
        Assert.That(repeated, Is.SameAs(graceful));
        yield return WaitForTask(graceful, "Escalated host shutdown did not complete.");

        Assert.That(IsFullyStopped(host.Manager), Is.True);
        Assert.That(clientStoppedCount, Is.EqualTo(1));
        Assert.That(serverStoppedCount, Is.EqualTo(1));

        Task afterStop = host.ConnectionService.ShutdownAndWaitAsync(
            NetworkShutdownMode.Immediate);
        Assert.That(afterStop.IsCompletedSuccessfully, Is.True);
        Assert.That(clientStoppedCount, Is.EqualTo(1));
        Assert.That(serverStoppedCount, Is.EqualTo(1));
    }

    private NetworkEndpointFixture CreateEndpoint(
        string name,
        bool includeConnectionService = true)
    {
        NetworkEndpointFixture endpoint = NetworkEndpointFixture.Create(
            name,
            includeConnectionService);
        endpoints.Add(endpoint);
        return endpoint;
    }

    private bool AreAllEndpointsStopped()
    {
        for (int i = 0; i < endpoints.Count; i++)
        {
            NetworkManager manager = endpoints[i].Manager;

            if (manager != null && !IsFullyStopped(manager))
                return false;
        }

        return true;
    }

    private static bool IsFullyStopped(NetworkManager manager)
    {
        return manager == null ||
               (!manager.IsListening &&
                !manager.IsClient &&
                !manager.IsServer &&
                !manager.ShutdownInProgress);
    }

    private static IEnumerator WaitForCondition(Func<bool> condition, string failureMessage)
    {
        float timeoutAt = Time.realtimeSinceStartup + OperationTimeoutSeconds;

        while (!condition.Invoke() && Time.realtimeSinceStartup < timeoutAt)
            yield return null;

        Assert.That(condition.Invoke(), Is.True, failureMessage);
    }

    private static IEnumerator WaitForTask(Task task, string failureMessage)
    {
        yield return WaitForCondition(() => task.IsCompleted, failureMessage);

        if (task.IsFaulted)
            Assert.Fail($"{failureMessage}\n{task.Exception}");

        Assert.That(task.IsCanceled, Is.False, failureMessage);
    }

    private sealed class NetworkEndpointFixture : IDisposable
    {
        private readonly GameObject root;
        private readonly NetworkConnectionConfig connectionConfig;

        private NetworkEndpointFixture(
            GameObject endpointRoot,
            NetworkConnectionConfig config,
            NetworkManager manager,
            UnityTransport transport,
            NetworkConnectionService connectionService)
        {
            root = endpointRoot;
            connectionConfig = config;
            Manager = manager;
            Transport = transport;
            ConnectionService = connectionService;
        }

        internal NetworkManager Manager { get; }
        internal UnityTransport Transport { get; }
        internal NetworkConnectionService ConnectionService { get; }

        internal static NetworkEndpointFixture Create(
            string name,
            bool includeConnectionService)
        {
            GameObject root = new(name);
            root.SetActive(false);

            UnityTransport transport = root.AddComponent<UnityTransport>();
            NetworkManager manager = root.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
                ProtocolVersion = 2
            };

            transport.SetConnectionData("127.0.0.1", 0, "127.0.0.1");

            NetworkConnectionConfig config = null;
            NetworkConnectionService connectionService = null;

            if (includeConnectionService)
            {
                config = CreateConnectionConfig();
                connectionService = root.AddComponent<NetworkConnectionService>();
                SetField(connectionService, "networkManager", manager);
                SetField(connectionService, "transport", transport);
                SetField(connectionService, "connectionConfig", config);
                SetField(connectionService, "shutdownTimeoutSeconds", 5f);
            }

            root.SetActive(true);

            if (connectionService != null)
            {
                Assert.That(connectionService.isActiveAndEnabled, Is.True);
            }

            return new NetworkEndpointFixture(
                root,
                config,
                manager,
                transport,
                connectionService);
        }

        public void Dispose()
        {
            if (root != null)
                UnityEngine.Object.DestroyImmediate(root);

            if (connectionConfig != null)
                UnityEngine.Object.DestroyImmediate(connectionConfig);
        }

        private static NetworkConnectionConfig CreateConnectionConfig()
        {
            NetworkConnectionConfig config =
                ScriptableObject.CreateInstance<NetworkConnectionConfig>();
            config.name = "Shutdown PlayMode Connection Config";
            SetField(config, "protocolVersion", (ushort)2);
            SetField(config, "hostAddress", "127.0.0.1");
            SetField(config, "port", (ushort)7777);
            SetField(config, "listenAddress", "127.0.0.1");
            SetField(config, "clientConnectionTimeoutSeconds", 5f);
            return config;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null, $"Missing field '{fieldName}'.");
            field.SetValue(target, value);
        }
    }
}
