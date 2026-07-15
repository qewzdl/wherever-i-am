using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class ClientReadinessNetworkProbe : NetworkBehaviour,
    IChatReadService,
    IChatCommandService,
    IMatchCompletionService,
    ISessionPhaseService,
    ISessionServiceReadiness
{
    private static readonly Dictionary<NetworkManager, SessionServiceRegistry>
        ClientRegistries = new();

    private readonly NetworkVariable<ProjectSceneKind> serverPhase = new(
        ProjectSceneKind.Unknown,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private SessionServiceRegistration registrations;

    public event Action MessagesChanged
    {
        add { }
        remove { }
    }

    public event Action<ChatMessageData> MessageAdded
    {
        add { }
        remove { }
    }

    public event Action AvailabilityChanged
    {
        add { }
        remove { }
    }

    public bool CanSubmitMessages => IsSpawned;
    public ChatChannel CurrentChannel => ChatChannel.Lobby;
    public int MessageCount => 0;
    public bool IsMatchRunning => IsSpawned;
    ProjectSceneKind ISessionPhaseService.ServerScenePhase => serverPhase.Value;
    bool ISessionServiceReadiness.IsSessionServiceReady =>
        IsSpawned && isActiveAndEnabled;

    internal static void SetClientRegistry(
        NetworkManager manager,
        SessionServiceRegistry registry)
    {
        ClientRegistries[manager] = registry;
    }

    internal static void ClearClientRegistries()
    {
        ClientRegistries.Clear();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer ||
            !ClientRegistries.TryGetValue(
                NetworkManager,
                out SessionServiceRegistry registry))
        {
            return;
        }

        if (!registry.TryRegister(
                registrar =>
                {
                    registrar.Register<IChatReadService>(this);
                    registrar.Register<IChatCommandService>(this);
                    registrar.Register<IMatchCompletionService>(this);
                    registrar.Register<ISessionPhaseService>(this);
                },
                out registrations,
                out Exception failure))
        {
            throw new InvalidOperationException(
                "Failed to register client readiness probe.",
                failure);
        }
    }

    public override void OnNetworkDespawn()
    {
        registrations?.Dispose();
        registrations = null;
    }

    internal void SetServerPhase(ProjectSceneKind phase)
    {
        Assert.That(IsServer, Is.True);
        serverPhase.Value = phase;
    }

    bool ISessionPhaseService.TrySetServerScenePhase(ProjectSceneKind sceneKind)
    {
        if (!IsServer)
            return false;

        serverPhase.Value = sceneKind;
        return true;
    }

    public ChatMessageData GetMessage(int index)
    {
        return default;
    }

    public bool TryGetMessage(uint messageId, out ChatMessageData message)
    {
        message = default;
        return false;
    }

    public bool IsLocalClient(ulong clientId)
    {
        return NetworkManager != null &&
               clientId == NetworkManager.LocalClientId;
    }

    public void SubmitMessage(string text)
    {
    }

    public bool CompleteMatchServerOnly(
        GameResultData matchResult,
        string reason)
    {
        return IsServer;
    }
}

public sealed class HostClientReadinessBootstrapPlayModeTests
{
    private const float TimeoutSeconds = 10f;

    private readonly List<Endpoint> endpoints = new();
    private readonly List<IDisposable> disposables = new();
    private GameObject networkPrefab;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        for (int i = 0; i < endpoints.Count; i++)
        {
            NetworkManager manager = endpoints[i].Manager;

            if (manager != null && manager.IsListening)
                manager.Shutdown(discardMessageQueue: true);
        }

        float timeout = Time.realtimeSinceStartup + TimeoutSeconds;

        while (!AllStopped() && Time.realtimeSinceStartup < timeout)
            yield return null;

        ClientReadinessNetworkProbe.ClearClientRegistries();

        for (int i = disposables.Count - 1; i >= 0; i--)
            disposables[i]?.Dispose();

        disposables.Clear();

        for (int i = endpoints.Count - 1; i >= 0; i--)
            endpoints[i].Dispose();

        endpoints.Clear();

        if (networkPrefab != null)
            UnityEngine.Object.DestroyImmediate(networkPrefab);

        networkPrefab = null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator HostAndClients_GateLobbyGameLateJoinAndContractLoss()
    {
        Endpoint host = CreateEndpoint("Readiness host");
        Endpoint client = CreateEndpoint("Readiness client");
        CreateAndRegisterNetworkPrefab(host, client);

        SessionFixture clientSession = CreateSession(client);
        Assert.That(
            client.SessionState.TryChangeState(
                NetworkSessionState.StartingClient,
                "Client bootstrap started."),
            Is.True);

        Assert.That(host.Manager.StartHost(), Is.True);
        yield return WaitForCondition(
            () => host.Manager.IsHost &&
                  host.Transport.GetLocalEndpoint().Port != 0,
            "Host did not start.");

        client.Transport.SetConnectionData(
            "127.0.0.1",
            host.Transport.GetLocalEndpoint().Port);
        Assert.That(client.Manager.StartClient(), Is.True);
        yield return WaitForCondition(
            () => client.Manager.IsConnectedClient,
            "Client did not connect to host.");

        Assert.That(
            ClientSessionReadinessGate.Begin(
                client.SessionState,
                ProjectSceneKind.Lobby,
                out string error),
            Is.True,
            error);
        Assert.That(
            ClientSessionReadinessGate.Commit(
                client.SessionState,
                ProjectSceneKind.Lobby,
                clientSession.Registry,
                out error),
            Is.False);
        Assert.That(
            client.SessionState.CurrentState,
            Is.EqualTo(NetworkSessionState.StartingClient));

        GameObject spawnedObject = UnityEngine.Object.Instantiate(networkPrefab);
        spawnedObject.name = "Spawned readiness probe";
        NetworkObject networkObject = spawnedObject.GetComponent<NetworkObject>();
        SetNetworkObjectField(
            networkObject,
            "NetworkManagerOwner",
            host.Manager);
        networkObject.Spawn();
        ClientReadinessNetworkProbe serverProbe =
            spawnedObject.GetComponent<ClientReadinessNetworkProbe>();
        serverProbe.SetServerPhase(ProjectSceneKind.Lobby);

        yield return WaitForCondition(
            () => IsReadyFor(clientSession.Registry, ProjectSceneKind.Lobby),
            "Client did not receive Lobby phase and dynamic contracts.");

        Assert.That(
            ClientSessionReadinessGate.Begin(
                client.SessionState,
                ProjectSceneKind.Lobby,
                out error),
            Is.True,
            error);
        Assert.That(
            ClientSessionReadinessGate.Commit(
                client.SessionState,
                ProjectSceneKind.Lobby,
                clientSession.Registry,
                out error),
            Is.True,
            error);
        Assert.That(
            client.SessionState.CurrentState,
            Is.EqualTo(NetworkSessionState.Lobby));

        Assert.That(
            ClientSessionReadinessGate.Begin(
                client.SessionState,
                ProjectSceneKind.Game,
                out error),
            Is.True,
            error);
        Assert.That(
            client.SessionState.CurrentState,
            Is.EqualTo(NetworkSessionState.LoadingGame));
        Assert.That(
            ClientSessionReadinessGate.Commit(
                client.SessionState,
                ProjectSceneKind.Game,
                clientSession.Registry,
                out error),
            Is.False);
        Assert.That(
            client.SessionState.CurrentState,
            Is.EqualTo(NetworkSessionState.LoadingGame));

        serverProbe.SetServerPhase(ProjectSceneKind.Game);
        yield return WaitForCondition(
            () => HasServerPhase(clientSession.Registry, ProjectSceneKind.Game),
            "Client did not receive Game phase.");

        Assert.That(
            ClientSessionReadinessGate.Commit(
                client.SessionState,
                ProjectSceneKind.Game,
                clientSession.Registry,
                out error),
            Is.True,
            error);
        Assert.That(
            client.SessionState.CurrentState,
            Is.EqualTo(NetworkSessionState.InGame));

        Endpoint lateClient = CreateEndpoint("Late readiness client");
        RegisterNetworkPrefab(lateClient);
        SessionFixture lateSession = CreateSession(lateClient);
        Assert.That(
            lateClient.SessionState.TryChangeState(
                NetworkSessionState.StartingClient,
                "Late client bootstrap started."),
            Is.True);
        lateClient.Transport.SetConnectionData(
            "127.0.0.1",
            host.Transport.GetLocalEndpoint().Port);
        Assert.That(lateClient.Manager.StartClient(), Is.True);

        yield return WaitForCondition(
            () => lateClient.Manager.IsConnectedClient &&
                  IsReadyFor(lateSession.Registry, ProjectSceneKind.Game),
            "Late client did not synchronize current Game readiness.");

        Assert.That(
            ClientSessionReadinessGate.Begin(
                lateClient.SessionState,
                ProjectSceneKind.Game,
                out error),
            Is.True,
            error);
        Assert.That(
            ClientSessionReadinessGate.Commit(
                lateClient.SessionState,
                ProjectSceneKind.Game,
                lateSession.Registry,
                out error),
            Is.True,
            error);
        Assert.That(
            lateClient.SessionState.CurrentState,
            Is.EqualTo(NetworkSessionState.InGame));

        client.GameState.ChangeState(GameState.InGame);
        int readinessLossCount = 0;
        SessionServiceReadinessMonitor monitor = new(
            clientSession.Registry,
            client.GameState,
            _ => readinessLossCount++);
        disposables.Add(monitor);

        networkObject.Despawn(true);
        yield return WaitForCondition(
            () => readinessLossCount == 1,
            "Client did not detect loss of active Game contracts.");

        Assert.That(
            clientSession.Registry.TryResolve(out IMatchCompletionService _),
            Is.False);
    }

    private Endpoint CreateEndpoint(string name)
    {
        Endpoint endpoint = Endpoint.Create(name);
        endpoints.Add(endpoint);
        return endpoint;
    }

    private SessionFixture CreateSession(Endpoint endpoint)
    {
        ServiceScope global = new($"{endpoint.Manager.name} Global");
        ServiceScope session = global.CreateChild(
            $"{endpoint.Manager.name} Session",
            SessionContractPolicy.Instance);
        SessionServiceRegistry registry = new(session);
        SessionFixture fixture = new(global, registry);
        disposables.Add(fixture);
        ClientReadinessNetworkProbe.SetClientRegistry(endpoint.Manager, registry);
        return fixture;
    }

    private void CreateAndRegisterNetworkPrefab(params Endpoint[] targets)
    {
        networkPrefab = new GameObject("Client readiness network prefab");
        NetworkObject networkObject = networkPrefab.AddComponent<NetworkObject>();
        SetNetworkObjectField(
            networkObject,
            "GlobalObjectIdHash",
            0x51A7E001u);
        PropertyInfo sceneObjectProperty = typeof(NetworkObject).GetProperty(
            nameof(NetworkObject.IsSceneObject),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(sceneObjectProperty, Is.Not.Null);
        sceneObjectProperty.SetValue(networkObject, false);
        networkPrefab.AddComponent<ClientReadinessNetworkProbe>();

        for (int i = 0; i < targets.Length; i++)
            RegisterNetworkPrefab(targets[i]);
    }

    private void RegisterNetworkPrefab(Endpoint endpoint)
    {
        endpoint.Manager.NetworkConfig.Prefabs.Add(
            new NetworkPrefab { Prefab = networkPrefab });
    }

    private static void SetNetworkObjectField(
        NetworkObject networkObject,
        string fieldName,
        object value)
    {
        FieldInfo field = typeof(NetworkObject).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing NetworkObject.{fieldName}.");
        field.SetValue(networkObject, value);
    }

    private static bool IsReadyFor(
        IServiceResolver services,
        ProjectSceneKind sceneKind)
    {
        return SessionServiceReadinessPolicy.Validate(
                   sceneKind,
                   services,
                   out _) &&
               SessionServiceReadinessPolicy.ValidateServerPhase(
                   sceneKind,
                   services,
                   out _);
    }

    private static bool HasServerPhase(
        IServiceResolver services,
        ProjectSceneKind expected)
    {
        return services.TryResolve(out ISessionPhaseService phase) &&
               phase.ServerScenePhase == expected;
    }

    private bool AllStopped()
    {
        for (int i = 0; i < endpoints.Count; i++)
        {
            NetworkManager manager = endpoints[i].Manager;

            if (manager != null &&
                (manager.IsListening || manager.IsClient || manager.IsServer ||
                 manager.ShutdownInProgress))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerator WaitForCondition(
        Func<bool> condition,
        string failureMessage)
    {
        float timeout = Time.realtimeSinceStartup + TimeoutSeconds;

        while (!condition.Invoke() && Time.realtimeSinceStartup < timeout)
            yield return null;

        Assert.That(condition.Invoke(), Is.True, failureMessage);
    }

    private sealed class SessionFixture : IDisposable
    {
        private readonly ServiceScope global;

        internal SessionFixture(
            ServiceScope globalScope,
            SessionServiceRegistry registry)
        {
            global = globalScope;
            Registry = registry;
        }

        internal SessionServiceRegistry Registry { get; }

        public void Dispose()
        {
            Registry.Dispose();
            global.Dispose();
        }
    }

    private sealed class Endpoint : IDisposable
    {
        private readonly GameObject root;

        private Endpoint(
            GameObject endpointRoot,
            NetworkManager manager,
            UnityTransport transport,
            NetworkSessionStateMachine sessionState,
            GameStateMachine gameState)
        {
            root = endpointRoot;
            Manager = manager;
            Transport = transport;
            SessionState = sessionState;
            GameState = gameState;
        }

        internal NetworkManager Manager { get; }
        internal UnityTransport Transport { get; }
        internal NetworkSessionStateMachine SessionState { get; }
        internal GameStateMachine GameState { get; }

        internal static Endpoint Create(string name)
        {
            GameObject root = new(name);
            UnityTransport transport = root.AddComponent<UnityTransport>();
            NetworkManager manager = root.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
                ProtocolVersion = 2
            };
            transport.SetConnectionData("127.0.0.1", 0, "127.0.0.1");

            NetworkSessionStateMachine sessionState =
                root.AddComponent<NetworkSessionStateMachine>();
            GameStateMachine gameState = root.AddComponent<GameStateMachine>();
            return new Endpoint(root, manager, transport, sessionState, gameState);
        }

        public void Dispose()
        {
            if (root != null)
                UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
