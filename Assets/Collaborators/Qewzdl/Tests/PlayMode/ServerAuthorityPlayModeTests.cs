using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

[Category("Multiplayer")]
public sealed class ServerAuthorityPlayModeTests
{
    private const float TimeoutSeconds = 10f;
    private const int SettleFrames = 20;
    private const int HandleItemId = 7101;
    private const int FirstMapId = 7;
    private const int SecondMapId = 8;
    private const int FirstGameModeId = 2;
    private const int SecondGameModeId = 3;
    private const uint DoorPrefabHash = 0x5EA10001u;
    private const uint HandlePrefabHash = 0x5EA10002u;
    private const uint PlayerPrefabHash = 0x5EA10003u;
    private const uint LobbyPrefabHash = 0x5EA10004u;

    private readonly List<Endpoint> endpoints = new();
    private readonly List<Object> cleanup = new();

    private Endpoint server;
    private Endpoint clientA;
    private Endpoint clientB;
    private GameObject doorPrefab;
    private GameObject handlePrefab;
    private GameObject playerPrefab;
    private GameObject lobbyPrefab;
    private LobbySessionServiceProbe sessionProbe;
    private ulong lobbyObjectId;

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

        while (!AllEndpointsStopped() && Time.realtimeSinceStartup < timeout)
            yield return null;

        for (int i = endpoints.Count - 1; i >= 0; i--)
            endpoints[i].Dispose();

        endpoints.Clear();

        for (int i = cleanup.Count - 1; i >= 0; i--)
        {
            if (cleanup[i] != null)
                Object.DestroyImmediate(cleanup[i]);
        }

        cleanup.Clear();
        server = null;
        clientA = null;
        clientB = null;
        doorPrefab = null;
        handlePrefab = null;
        playerPrefab = null;
        lobbyPrefab = null;
        sessionProbe = null;
        yield return null;
    }

    // The insert RPC is open to every client and the handle item was only ever
    // checked on the sending client, so the door had to start checking who
    // actually carries the handle.
    [UnityTest]
    public IEnumerator HandleInsert_IsRefusedForAClientThatCarriesNoHandle()
    {
        yield return StartNetwork();

        ulong doorId = SpawnDoorOnServer(requiredHandleCount: 1);
        ulong handleId = SpawnHandleOnServer();
        yield return WaitForSpawnOnEveryEndpoint(doorId);
        yield return WaitForSpawnOnEveryEndpoint(handleId);

        EntranceDoor serverDoor = GetSpawned<EntranceDoor>(server, doorId);
        EntranceDoor doorOnClientA = GetSpawned<EntranceDoor>(clientA, doorId);
        EntranceDoor doorOnClientB = GetSpawned<EntranceDoor>(clientB, doorId);

        GiveHandleToClient(handleId, clientA.Manager.LocalClientId);
        yield return WaitForCondition(
            () => GetSpawned<NetworkItemTestPickup>(server, handleId).OwnerClientId ==
                  clientA.Manager.LocalClientId,
            "Handle item did not reach client A on the server.");

        LogAssert.Expect(
            LogType.Warning,
            new Regex(
                "EntranceDoor rejected handle insert from client " +
                $"{clientB.Manager.LocalClientId}: it does not carry item {HandleItemId}\\."));

        doorOnClientB.TryInsertHandle(HandleItemId);
        yield return WaitFrames(SettleFrames);

        Assert.That(serverDoor.IsUnlocked, Is.False);
        Assert.That(serverDoor.InsertedHandleCount, Is.EqualTo(0));

        ulong unlockedBy = ulong.MaxValue;
        int unlockedCount = 0;
        serverDoor.Unlocked += instigator =>
        {
            unlockedBy = instigator;
            unlockedCount++;
        };

        doorOnClientA.TryInsertHandle(HandleItemId);

        yield return WaitForCondition(
            () => serverDoor.IsUnlocked && doorOnClientB.IsUnlocked,
            "The client carrying the handle could not unlock the door.");

        Assert.That(unlockedCount, Is.EqualTo(1));
        Assert.That(unlockedBy, Is.EqualTo(clientA.Manager.LocalClientId));
        Assert.That(serverDoor.InsertedHandleCount, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator HandleInsert_TakesOneHandlePerCarrierAndUnlocksExactlyOnce()
    {
        yield return StartNetwork();

        ulong doorId = SpawnDoorOnServer(requiredHandleCount: 2);
        ulong handleAId = SpawnHandleOnServer();
        ulong handleBId = SpawnHandleOnServer();
        yield return WaitForSpawnOnEveryEndpoint(doorId);
        yield return WaitForSpawnOnEveryEndpoint(handleAId);
        yield return WaitForSpawnOnEveryEndpoint(handleBId);

        EntranceDoor serverDoor = GetSpawned<EntranceDoor>(server, doorId);
        EntranceDoor doorOnClientA = GetSpawned<EntranceDoor>(clientA, doorId);
        EntranceDoor doorOnClientB = GetSpawned<EntranceDoor>(clientB, doorId);

        GiveHandleToClient(handleAId, clientA.Manager.LocalClientId);
        GiveHandleToClient(handleBId, clientB.Manager.LocalClientId);

        int unlockedCount = 0;
        int insertedCount = 0;
        serverDoor.Unlocked += _ => unlockedCount++;
        serverDoor.HandleInserted += (_, _, _, _) => insertedCount++;

        doorOnClientA.TryInsertHandle(HandleItemId);
        yield return WaitForCondition(
            () => serverDoor.InsertedHandleCount == 1,
            "The first handle was not accepted.");

        Assert.That(serverDoor.IsUnlocked, Is.False);
        Assert.That(unlockedCount, Is.EqualTo(0));

        doorOnClientB.TryInsertHandle(HandleItemId);
        yield return WaitForCondition(
            () => serverDoor.IsUnlocked,
            "The second handle did not unlock the door.");

        Assert.That(serverDoor.InsertedHandleCount, Is.EqualTo(2));
        Assert.That(insertedCount, Is.EqualTo(2));

        doorOnClientA.TryInsertHandle(HandleItemId);
        yield return WaitFrames(SettleFrames);

        Assert.That(unlockedCount, Is.EqualTo(1));
        Assert.That(insertedCount, Is.EqualTo(2));
        Assert.That(serverDoor.InsertedHandleCount, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator PlayerObjectOwnership_IsGrantedOncePerConnectedClient()
    {
        yield return StartNetwork();

        GameObject serviceHost = Track(new GameObject("Player ownership service"));
        serviceHost.SetActive(false);
        NetworkPlayerOwnershipService ownershipService =
            serviceHost.AddComponent<NetworkPlayerOwnershipService>();
        PlayModeTestReflection.SetField(
            ownershipService,
            "networkManager",
            server.Manager);
        serviceHost.SetActive(true);

        ulong clientId = clientA.Manager.LocalClientId;
        Assert.That(ownershipService.CanSpawnPlayerObjectFor(clientId), Is.True);

        NetworkObject first = CreatePlayerInstance();
        Assert.That(ownershipService.TrySpawnAsPlayerObject(first, clientId), Is.True);

        yield return WaitForCondition(
            () => server.Manager.ConnectedClients[clientId].PlayerObject == first,
            "The player object was not assigned to its client.");

        Assert.That(ownershipService.CanSpawnPlayerObjectFor(clientId), Is.False);

        NetworkObject second = CreatePlayerInstance();
        Assert.That(
            ownershipService.TrySpawnAsPlayerObject(second, clientId),
            Is.False,
            "A client must not end up with a second player object.");
        Assert.That(second.IsSpawned, Is.False);

        LogAssert.Expect(LogType.Error, new Regex("Player object is already spawned\\."));
        Assert.That(ownershipService.TrySpawnAsPlayerObject(first, clientId), Is.False);

        LogAssert.Expect(
            LogType.Error,
            new Regex("NetworkPlayerOwnershipService received null player object\\."));
        Assert.That(ownershipService.TrySpawnAsPlayerObject(null, clientId), Is.False);

        LogAssert.Expect(
            LogType.Warning,
            new Regex("Cannot assign player ownership\\. Client '4242' is not connected\\."));
        Assert.That(ownershipService.CanSpawnPlayerObjectFor(4242), Is.False);
    }

    // Every lobby command RPC is open to all clients and the owner check lives
    // on the server side of it. Nothing pinned those checks down, so a
    // refactor could drop one without a single test noticing.
    [UnityTest]
    public IEnumerator LobbyCommands_AreRefusedForEveryoneButTheRoomOwner()
    {
        yield return StartHostedLobbyNetwork();

        LobbyState hostLobby = GetSpawned<LobbyState>(server, lobbyObjectId);
        LobbyController hostController = GetSpawned<LobbyController>(server, lobbyObjectId);
        LobbyController controllerOnClient =
            GetSpawned<LobbyController>(clientA, lobbyObjectId);
        ulong ownerClientId = server.Manager.LocalClientId;
        ulong guestClientId = clientA.Manager.LocalClientId;

        Assert.That(hostLobby.RoomOwnerClientId.Value, Is.EqualTo(ownerClientId));
        Assert.That(hostLobby.Players.Count, Is.EqualTo(2));
        Assert.That(hostLobby.Settings.Value.MapId, Is.EqualTo(FirstMapId));
        Assert.That(hostLobby.Settings.Value.GameModeId, Is.EqualTo(FirstGameModeId));

        LogAssert.Expect(LogType.Warning, "Only room owner can change lobby settings.");
        LogAssert.Expect(LogType.Warning, "Only room owner can change lobby settings.");
        LogAssert.Expect(LogType.Warning, "Only room owner can request game start.");

        controllerOnClient.RequestSetMapRpc(SecondMapId);
        controllerOnClient.RequestSetGameModeRpc(SecondGameModeId);
        controllerOnClient.RequestStartGameRpc();
        yield return WaitFrames(SettleFrames);

        Assert.That(hostLobby.Settings.Value.MapId, Is.EqualTo(FirstMapId));
        Assert.That(hostLobby.Settings.Value.GameModeId, Is.EqualTo(FirstGameModeId));
        Assert.That(hostLobby.Phase.Value, Is.EqualTo(LobbyPhase.Open));
        Assert.That(sessionProbe.StartGameCount, Is.Zero);

        // Readiness is the one thing a guest owns, and only its own.
        controllerOnClient.RequestSetReadyRpc(true);
        yield return WaitForCondition(
            () => IsPlayerReady(hostLobby, guestClientId),
            "A client could not mark itself ready.");

        Assert.That(IsPlayerReady(hostLobby, ownerClientId), Is.False);

        hostController.RequestSetMapRpc(SecondMapId);
        hostController.RequestSetGameModeRpc(SecondGameModeId);
        hostController.RequestSetReadyRpc(true);
        yield return WaitForCondition(
            () => hostLobby.Settings.Value.MapId == SecondMapId &&
                  IsPlayerReady(hostLobby, ownerClientId),
            "The room owner could not change the lobby it owns.");

        Assert.That(hostLobby.Settings.Value.GameModeId, Is.EqualTo(SecondGameModeId));

        hostController.RequestStartGameRpc();
        yield return WaitForCondition(
            () => hostLobby.Phase.Value == LobbyPhase.Starting,
            "The room owner could not start the match.");

        Assert.That(sessionProbe.StartGameCount, Is.EqualTo(1));
        Assert.That(sessionProbe.LastMapId, Is.EqualTo(SecondMapId));
    }

    private static bool IsPlayerReady(LobbyState lobby, ulong clientId)
    {
        for (int i = 0; i < lobby.Players.Count; i++)
        {
            if (lobby.Players[i].ClientId == clientId)
                return lobby.Players[i].IsReady;
        }

        return false;
    }

    private IEnumerator StartHostedLobbyNetwork()
    {
        CreateLobbyPrefab();
        server = CreateEndpoint("Lobby host");
        clientA = CreateEndpoint("Lobby client");
        RegisterLobbyPrefab(server, clientA);

        Assert.That(server.Manager.StartHost(), Is.True);
        yield return WaitForCondition(
            () => server.Manager.IsHost &&
                  server.Transport.GetLocalEndpoint().Port != 0,
            "Lobby host did not start.");

        // Spawned before anyone joins: the controller only starts tracking
        // players once it is on the network.
        GameObject instance = Track(Object.Instantiate(lobbyPrefab));
        instance.name = "Lobby spawned";
        sessionProbe = new LobbySessionServiceProbe();
        Assert.That(
            instance.GetComponent<LobbyController>().Construct(
                sessionProbe,
                new LobbyAdmissionServiceProbe()),
            Is.True);
        lobbyObjectId = SpawnOnServer(instance);

        clientA.Transport.SetConnectionData(
            "127.0.0.1",
            server.Transport.GetLocalEndpoint().Port);
        Assert.That(clientA.Manager.StartClient(), Is.True);

        yield return WaitForCondition(
            () => clientA.Manager.IsConnectedClient &&
                  server.Manager.ConnectedClientsIds.Count == 2 &&
                  HasSpawnedObject(clientA, lobbyObjectId),
            "The lobby client did not join the host.");
    }

    private void CreateLobbyPrefab()
    {
        GameMapDefinition firstMap = CreateMapDefinition(FirstMapId, "First");
        GameMapDefinition secondMap = CreateMapDefinition(SecondMapId, "Second");
        GameMapCatalog catalog = Track(ScriptableObject.CreateInstance<GameMapCatalog>());
        PlayModeTestReflection.SetField(catalog, "defaultMapId", FirstMapId);
        PlayModeTestReflection.SetField(catalog, "maps", new[] { firstMap, secondMap });

        LobbyConfig config = Track(ScriptableObject.CreateInstance<LobbyConfig>());
        PlayModeTestReflection.SetField(config, "minPlayersToStart", 2);
        PlayModeTestReflection.SetField(config, "maxPlayers", 4);
        PlayModeTestReflection.SetField(config, "requireAllPlayersReady", true);
        PlayModeTestReflection.SetField(
            config,
            "gameModeIds",
            new[] { FirstGameModeId, SecondGameModeId });
        PlayModeTestReflection.SetField(config, "mapCatalog", catalog);

        lobbyPrefab = Track(new GameObject("Lobby test prefab"));
        lobbyPrefab.SetActive(false);
        ConfigureNetworkObject(
            lobbyPrefab.AddComponent<NetworkObject>(),
            LobbyPrefabHash);
        lobbyPrefab.AddComponent<LobbyState>();
        PlayModeTestReflection.SetField(
            lobbyPrefab.AddComponent<LobbyController>(),
            "lobbyConfig",
            config);
        lobbyPrefab.SetActive(true);
    }

    private GameMapDefinition CreateMapDefinition(int mapId, string displayName)
    {
        GameMapDefinition map = Track(ScriptableObject.CreateInstance<GameMapDefinition>());
        PlayModeTestReflection.SetField(map, "mapId", mapId);
        PlayModeTestReflection.SetField(map, "displayName", displayName);
        PlayModeTestReflection.SetField(map, "sceneName", $"Map_{mapId}");
        PlayModeTestReflection.SetField(
            map,
            "scenePath",
            $"Assets/Scenes/Map_{mapId}.unity");
        return map;
    }

    private void RegisterLobbyPrefab(params Endpoint[] targets)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            targets[i].Manager.NetworkConfig.Prefabs.Add(
                new NetworkPrefab { Prefab = lobbyPrefab });
        }
    }

    private IEnumerator StartNetwork()
    {
        CreateNetworkPrefabs();
        server = CreateEndpoint("Authority dedicated server");
        clientA = CreateEndpoint("Authority client A");
        clientB = CreateEndpoint("Authority client B");
        RegisterPrefabs(server, clientA, clientB);

        Assert.That(server.Manager.StartServer(), Is.True);
        yield return WaitForCondition(
            () => server.Manager.IsServer &&
                  server.Transport.GetLocalEndpoint().Port != 0,
            "Authority test server did not start.");

        ushort port = server.Transport.GetLocalEndpoint().Port;
        clientA.Transport.SetConnectionData("127.0.0.1", port);
        clientB.Transport.SetConnectionData("127.0.0.1", port);

        Assert.That(clientA.Manager.StartClient(), Is.True);
        Assert.That(clientB.Manager.StartClient(), Is.True);

        yield return WaitForCondition(
            () => clientA.Manager.IsConnectedClient &&
                  clientB.Manager.IsConnectedClient &&
                  server.Manager.ConnectedClientsIds.Count == 2,
            "Both authority test clients did not connect.");
    }

    private void CreateNetworkPrefabs()
    {
        doorPrefab = Track(new GameObject("Entrance door test prefab"));
        doorPrefab.SetActive(false);
        ConfigureNetworkObject(doorPrefab.AddComponent<NetworkObject>(), DoorPrefabHash);
        doorPrefab.AddComponent<EntranceDoor>();
        doorPrefab.SetActive(true);

        playerPrefab = Track(new GameObject("Authority player test prefab"));
        playerPrefab.SetActive(false);
        ConfigureNetworkObject(playerPrefab.AddComponent<NetworkObject>(), PlayerPrefabHash);
        playerPrefab.SetActive(true);

        handlePrefab = Track(new GameObject("Handle item test prefab"));
        handlePrefab.SetActive(false);
        handlePrefab.transform.position = new Vector3(10000f, 10000f, 10000f);
        ConfigureNetworkObject(handlePrefab.AddComponent<NetworkObject>(), HandlePrefabHash);

        Rigidbody body = handlePrefab.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        NetworkTransform networkTransform = handlePrefab.AddComponent<NetworkTransform>();
        networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
        handlePrefab.AddComponent<NetworkRigidbody>();
        handlePrefab.AddComponent<BoxCollider>();

        // DraggableObject.Awake reads the item data, so it has to be in place
        // before the prefab is ever activated.
        PickupItemData data = Track(ScriptableObject.CreateInstance<PickupItemData>());
        data.ItemID = HandleItemId;
        data.Mass = 1f;
        data.MaxFollowSpeed = 15f;
        data.FollowSpeedMultiplier = 2f;
        data.MaxDragDistance = 20f;
        data.ThrowVelocitySamples = 3f;
        data.MinDistance = 0f;
        PlayModeTestReflection.SetField(
            handlePrefab.AddComponent<NetworkItemTestPickup>(),
            "data",
            data);
        handlePrefab.SetActive(true);
    }

    private ulong SpawnDoorOnServer(int requiredHandleCount)
    {
        GameObject instance = Track(Object.Instantiate(doorPrefab));
        instance.name = "Entrance door spawned";
        EntranceDoor door = instance.GetComponent<EntranceDoor>();
        PlayModeTestReflection.SetField(door, "requireHandleItem", true);
        PlayModeTestReflection.SetField(door, "requiredHandleItemId", HandleItemId);
        PlayModeTestReflection.SetField(door, "requiredHandleCount", requiredHandleCount);
        PlayModeTestReflection.SetField(door, "consumeInsertedHandle", false);
        PlayModeTestReflection.SetField(door, "despawnNetworkObjectWhenUnlocked", false);
        return SpawnOnServer(instance);
    }

    private ulong SpawnHandleOnServer()
    {
        GameObject instance = Track(Object.Instantiate(handlePrefab));
        instance.name = "Handle item spawned";
        return SpawnOnServer(instance);
    }

    // The pickup handshake itself is covered by the item ownership tests; here
    // only its outcome matters - this client is holding that item.
    private void GiveHandleToClient(ulong networkObjectId, ulong clientId)
    {
        NetworkItemTestPickup item =
            GetSpawned<NetworkItemTestPickup>(server, networkObjectId);
        item.NetworkObject.ChangeOwnership(clientId);
        PlayModeTestReflection
            .GetField<NetworkVariable<bool>>(item, "netIsPickedUp")
            .Value = true;
    }

    private NetworkObject CreatePlayerInstance()
    {
        GameObject instance = Track(Object.Instantiate(playerPrefab));
        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        PlayModeTestReflection.SetField(
            networkObject,
            "NetworkManagerOwner",
            server.Manager);
        return networkObject;
    }

    private ulong SpawnOnServer(GameObject instance)
    {
        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        PlayModeTestReflection.SetField(
            networkObject,
            "NetworkManagerOwner",
            server.Manager);
        networkObject.Spawn();
        Assert.That(networkObject.IsSpawned, Is.True);
        return networkObject.NetworkObjectId;
    }

    private void RegisterPrefabs(params Endpoint[] targets)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            targets[i].Manager.NetworkConfig.Prefabs.Add(
                new NetworkPrefab { Prefab = doorPrefab });
            targets[i].Manager.NetworkConfig.Prefabs.Add(
                new NetworkPrefab { Prefab = handlePrefab });
            targets[i].Manager.NetworkConfig.Prefabs.Add(
                new NetworkPrefab { Prefab = playerPrefab });
        }
    }

    private IEnumerator WaitForSpawnOnEveryEndpoint(ulong networkObjectId)
    {
        yield return WaitForCondition(
            () => HasSpawnedObject(server, networkObjectId) &&
                  HasSpawnedObject(clientA, networkObjectId) &&
                  HasSpawnedObject(clientB, networkObjectId),
            $"Network object {networkObjectId} was not spawned on every endpoint.");
    }

    private static bool HasSpawnedObject(Endpoint endpoint, ulong networkObjectId)
    {
        return endpoint?.Manager?.SpawnManager != null &&
               endpoint.Manager.SpawnManager.SpawnedObjects.ContainsKey(networkObjectId);
    }

    private static T GetSpawned<T>(Endpoint endpoint, ulong networkObjectId)
        where T : Component
    {
        Assert.That(
            endpoint.Manager.SpawnManager.SpawnedObjects.TryGetValue(
                networkObjectId,
                out NetworkObject networkObject),
            Is.True);
        T component = networkObject.GetComponent<T>();
        Assert.That(component, Is.Not.Null);
        return component;
    }

    private static void ConfigureNetworkObject(NetworkObject networkObject, uint hash)
    {
        PlayModeTestReflection.SetField(networkObject, "GlobalObjectIdHash", hash);
        PropertyInfo sceneObjectProperty = typeof(NetworkObject).GetProperty(
            nameof(NetworkObject.IsSceneObject),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(sceneObjectProperty, Is.Not.Null);
        sceneObjectProperty.SetValue(networkObject, false);
    }

    private Endpoint CreateEndpoint(string name)
    {
        Endpoint endpoint = Endpoint.Create(name);
        endpoints.Add(endpoint);
        return endpoint;
    }

    private bool AllEndpointsStopped()
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

    private static IEnumerator WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
            yield return null;
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

    private T Track<T>(T value)
        where T : Object
    {
        cleanup.Add(value);
        return value;
    }

    private sealed class Endpoint : IDisposable
    {
        private readonly GameObject root;

        private Endpoint(
            GameObject endpointRoot,
            NetworkManager manager,
            UnityTransport transport)
        {
            root = endpointRoot;
            Manager = manager;
            Transport = transport;
        }

        internal NetworkManager Manager { get; }
        internal UnityTransport Transport { get; }

        internal static Endpoint Create(string name)
        {
            GameObject root = new(name);
            UnityTransport transport = root.AddComponent<UnityTransport>();
            NetworkManager manager = root.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
                ProtocolVersion = 6
            };
            transport.SetConnectionData("127.0.0.1", 0, "127.0.0.1");
            return new Endpoint(root, manager, transport);
        }

        public void Dispose()
        {
            if (root != null)
                Object.DestroyImmediate(root);
        }
    }
}
