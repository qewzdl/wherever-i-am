using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class NetworkItemTestDraggable : DraggableObject
{
}

public sealed class NetworkItemTestPickup : PassiveItem
{
    public override void Activate()
    {
    }
}

[Category("Multiplayer")]
public sealed class TwoClientItemOwnershipPlayModeTests
{
    private const float TimeoutSeconds = 10f;
    private const float InitialPlayerSpeed = 10f;
    private const float ItemMass = 2f;
    private const uint DraggablePrefabHash = 0x17A60001u;
    private const uint PickupPrefabHash = 0x17A60002u;
    private const uint PlayerPrefabHash = 0x17A60003u;

    private readonly List<Endpoint> endpoints = new();
    private readonly List<Object> cleanup = new();

    private Endpoint server;
    private Endpoint clientA;
    private Endpoint clientB;
    private GameObject draggablePrefab;
    private GameObject pickupPrefab;
    private GameObject networkTestPlayerPrefab;

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

        DraggableObject.ActiveDraggedObjects.Clear();

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
        draggablePrefab = null;
        pickupPrefab = null;
        networkTestPlayerPrefab = null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator PickupDrop_TransfersOwnershipBetweenClientsAndRecoversOnDisconnect()
    {
        yield return StartNetwork();

        Vector3 spawnPosition = new(0f, 2f, 0f);
        ulong networkObjectId = SpawnOnServer<NetworkItemTestPickup>(
            pickupPrefab,
            spawnPosition);
        yield return WaitForSpawnOnEveryEndpoint(networkObjectId);

        NetworkTestPlayer playerA = GetNetworkTestPlayer(clientA);
        NetworkTestPlayer playerB = GetNetworkTestPlayer(clientB);
        NetworkItemTestPickup itemA =
            GetSpawnedComponent<NetworkItemTestPickup>(clientA, networkObjectId);
        NetworkItemTestPickup itemB =
            GetSpawnedComponent<NetworkItemTestPickup>(clientB, networkObjectId);
        NetworkItemTestPickup serverItem =
            GetSpawnedComponent<NetworkItemTestPickup>(server, networkObjectId);
        ItemNavigationObstacle serverNavigation =
            serverItem.GetComponent<ItemNavigationObstacle>();
        ItemNavigationObstacle clientNavigation =
            itemA.GetComponent<ItemNavigationObstacle>();

        yield return WaitForCondition(
            () => serverNavigation.IsBlockingNavigation &&
                  !clientNavigation.IsBlockingNavigation,
            "Dropped pickup navigation obstacle was not server-only.");

        AssertServerCanReach(clientA.Manager.LocalClientId, serverItem);
        BeginPickupRequest(itemA, playerA);

        yield return WaitForCondition(
            () => serverItem.OwnerClientId == clientA.Manager.LocalClientId &&
                  IsPickedUp(serverItem) &&
                  playerA.Orchestrator.States.IsCarrying &&
                  !serverNavigation.IsBlockingNavigation,
            "Client A did not receive pickup ownership and confirmation.");

        Assert.That(itemA.GetComponent<Rigidbody>().isKinematic, Is.True);
        Assert.That(playerB.Orchestrator.States.IsCarrying, Is.False);

        itemA.OnDrop();

        yield return WaitForCondition(
            () => !IsPickedUp(serverItem) &&
                  !playerA.Orchestrator.States.IsCarrying &&
                  serverNavigation.IsBlockingNavigation,
            "Drop did not clear replicated pickup and local carrying state.");

        Assert.That(
            serverItem.OwnerClientId,
            Is.EqualTo(clientA.Manager.LocalClientId),
            "A dropped item remains owned by its last owner until another pickup.");
        Assert.That(itemA.GetComponent<Rigidbody>().isKinematic, Is.False);
        Assert.That(itemA.GetComponent<Rigidbody>().useGravity, Is.True);

        AssertServerCanReach(clientB.Manager.LocalClientId, serverItem);
        BeginPickupRequest(itemB, playerB);

        yield return WaitForCondition(
            () => serverItem.OwnerClientId == clientB.Manager.LocalClientId &&
                  IsPickedUp(serverItem) &&
                  playerB.Orchestrator.States.IsCarrying,
            "Client B did not take ownership after Client A dropped the item.");

        Assert.That(playerA.Orchestrator.States.IsCarrying, Is.False);

        clientB.Manager.Shutdown(discardMessageQueue: false);

        yield return WaitForCondition(
            () => !clientB.Manager.IsListening &&
                  serverItem.OwnerClientId == NetworkManager.ServerClientId &&
                  !IsPickedUp(serverItem) &&
                  itemA.OwnerClientId == NetworkManager.ServerClientId,
            "Disconnect did not return the picked item to server ownership.");

        Assert.That(
            Vector3.Distance(serverItem.transform.position, spawnPosition),
            Is.LessThan(0.05f));
        Assert.That(serverItem.GetComponent<Rigidbody>().isKinematic, Is.False);
        Assert.That(serverItem.GetComponent<Rigidbody>().useGravity, Is.True);
        Assert.That(
            GetSpawnedComponent<NetworkItemTestPickup>(
                    clientA,
                    networkObjectId)
                .OwnerClientId,
            Is.EqualTo(NetworkManager.ServerClientId));
    }

    [UnityTest]
    public IEnumerator SimultaneousPickupRequests_HaveOneWinnerAndLoserCanRetry()
    {
        yield return StartNetwork();

        ulong networkObjectId = SpawnOnServer<NetworkItemTestPickup>(
            pickupPrefab,
            new Vector3(0f, 2f, 0f));
        yield return WaitForSpawnOnEveryEndpoint(networkObjectId);

        NetworkTestPlayer playerA = GetNetworkTestPlayer(clientA);
        NetworkTestPlayer playerB = GetNetworkTestPlayer(clientB);
        NetworkItemTestPickup itemA =
            GetSpawnedComponent<NetworkItemTestPickup>(
                clientA,
                networkObjectId);
        NetworkItemTestPickup itemB =
            GetSpawnedComponent<NetworkItemTestPickup>(
                clientB,
                networkObjectId);
        NetworkItemTestPickup serverItem =
            GetSpawnedComponent<NetworkItemTestPickup>(
                server,
                networkObjectId);

        AssertServerCanReach(clientA.Manager.LocalClientId, serverItem);
        AssertServerCanReach(clientB.Manager.LocalClientId, serverItem);
        BeginPickupRequest(itemA, playerA);
        BeginPickupRequest(itemB, playerB);

        yield return WaitForCondition(
            () => IsPickedUp(serverItem) &&
                  playerA.Orchestrator.States.IsCarrying !=
                  playerB.Orchestrator.States.IsCarrying,
            "Concurrent pickup requests did not resolve to one winner.");

        bool clientAWon =
            serverItem.OwnerClientId == clientA.Manager.LocalClientId;
        Assert.That(
            clientAWon ||
            serverItem.OwnerClientId == clientB.Manager.LocalClientId,
            Is.True);

        NetworkTestPlayer winner = clientAWon ? playerA : playerB;
        NetworkTestPlayer loser = clientAWon ? playerB : playerA;
        NetworkItemTestPickup winnerItem = clientAWon ? itemA : itemB;
        NetworkItemTestPickup loserItem = clientAWon ? itemB : itemA;
        ulong loserClientId = clientAWon
            ? clientB.Manager.LocalClientId
            : clientA.Manager.LocalClientId;

        Assert.That(winner.Orchestrator.States.IsCarrying, Is.True);
        Assert.That(loser.Orchestrator.States.IsCarrying, Is.False);
        Assert.That(
            PlayModeTestReflection.GetField<bool>(
                winner.Interaction,
                "pickupRequestPending"),
            Is.False);
        Assert.That(
            PlayModeTestReflection.GetField<bool>(
                loser.Interaction,
                "pickupRequestPending"),
            Is.False);

        winnerItem.OnDrop();

        yield return WaitForCondition(
            () => !IsPickedUp(serverItem) &&
                  !winner.Orchestrator.States.IsCarrying,
            "Pickup winner did not release the item.");

        AssertServerCanReach(loserClientId, serverItem);
        BeginPickupRequest(loserItem, loser);

        yield return WaitForCondition(
            () => IsPickedUp(serverItem) &&
                  serverItem.OwnerClientId == loserClientId &&
                  loser.Orchestrator.States.IsCarrying,
            "Losing client could not retry pickup after the item was dropped.");
    }

    [UnityTest]
    public IEnumerator DragRelease_RestoresPhysicsPlayerSpeedAndReplicatedState()
    {
        yield return StartNetwork();

        ulong networkObjectId = SpawnOnServer<NetworkItemTestDraggable>(
            draggablePrefab,
            new Vector3(0f, 1f, 0f));
        yield return WaitForSpawnOnEveryEndpoint(networkObjectId);

        NetworkTestPlayer player = GetNetworkTestPlayer(clientA);
        NetworkItemTestDraggable clientItem =
            GetSpawnedComponent<NetworkItemTestDraggable>(
                clientA,
                networkObjectId);
        NetworkItemTestDraggable serverItem =
            GetSpawnedComponent<NetworkItemTestDraggable>(
                server,
                networkObjectId);
        Rigidbody clientBody = clientItem.GetComponent<Rigidbody>();
        ItemNavigationObstacle serverNavigation =
            serverItem.GetComponent<ItemNavigationObstacle>();
        ItemNavigationObstacle clientNavigation =
            clientItem.GetComponent<ItemNavigationObstacle>();

        yield return WaitForCondition(
            () => serverNavigation.IsBlockingNavigation &&
                  serverNavigation.GetComponent<NavMeshObstacle>().carving &&
                  !clientNavigation.IsBlockingNavigation,
            "Stationary draggable navigation obstacle was not server-only.");

        AssertServerCanReach(clientA.Manager.LocalClientId, serverItem);
        BeginDragRequest(
            clientItem,
            player,
            clientItem.transform.position);

        yield return WaitForCondition(
            () => serverItem.OwnerClientId == clientA.Manager.LocalClientId &&
                  IsDragging(serverItem) &&
                  player.Orchestrator.States.IsDragging &&
                  DraggableObject.ActiveDraggedObjects.Contains(clientItem) &&
                  !serverNavigation.IsBlockingNavigation &&
                  !serverNavigation.GetComponent<NavMeshObstacle>().enabled &&
                  !serverNavigation.GetComponent<NavMeshObstacle>().carving,
            "Client A did not start the authoritative drag.");

        Assert.That(player.Controller.GetSpeed(), Is.EqualTo(6.25f).Within(0.001f));
        Assert.That(clientBody.useGravity, Is.False);
        Assert.That(clientBody.linearDamping, Is.EqualTo(5f).Within(0.001f));
        Assert.That(clientBody.angularDamping, Is.EqualTo(10f).Within(0.001f));
        Assert.That(clientBody.mass, Is.LessThan(ItemMass));
        Assert.That(DraggableObject.ActiveDraggedObjects, Does.Contain(clientItem));

        clientItem.OnUninteract();

        yield return WaitForCondition(
            () => !IsDragging(serverItem) &&
                  !player.Orchestrator.States.IsDragging &&
                  !DraggableObject.ActiveDraggedObjects.Contains(clientItem) &&
                  serverNavigation.IsBlockingNavigation &&
                  serverNavigation.GetComponent<NavMeshObstacle>().carving,
            "Drag release left replicated or local dragging state active.");

        Assert.That(player.Controller.GetSpeed(), Is.EqualTo(InitialPlayerSpeed));
        Assert.That(clientBody.useGravity, Is.True);
        Assert.That(clientBody.linearDamping, Is.Zero);
        Assert.That(clientBody.angularDamping, Is.EqualTo(0.05f).Within(0.001f));
        Assert.That(clientBody.mass, Is.EqualTo(ItemMass).Within(0.001f));
        Assert.That(
            PlayModeTestReflection.GetField<bool>(
                player.Interaction,
                "dragRequestPending"),
            Is.False);
    }

    [UnityTest]
    public IEnumerator SimultaneousDragRequests_HaveOneWinnerAndDisconnectReleasesItem()
    {
        yield return StartNetwork();

        ulong networkObjectId = SpawnOnServer<NetworkItemTestDraggable>(
            draggablePrefab,
            new Vector3(0f, 1f, 0f));
        yield return WaitForSpawnOnEveryEndpoint(networkObjectId);

        NetworkTestPlayer playerA = GetNetworkTestPlayer(clientA);
        NetworkTestPlayer playerB = GetNetworkTestPlayer(clientB);
        NetworkItemTestDraggable itemA =
            GetSpawnedComponent<NetworkItemTestDraggable>(
                clientA,
                networkObjectId);
        NetworkItemTestDraggable itemB =
            GetSpawnedComponent<NetworkItemTestDraggable>(
                clientB,
                networkObjectId);
        NetworkItemTestDraggable serverItem =
            GetSpawnedComponent<NetworkItemTestDraggable>(
                server,
                networkObjectId);

        AssertServerCanReach(clientA.Manager.LocalClientId, serverItem);
        AssertServerCanReach(clientB.Manager.LocalClientId, serverItem);
        BeginDragRequest(itemA, playerA, itemA.transform.position);
        BeginDragRequest(itemB, playerB, itemB.transform.position);

        yield return WaitForCondition(
            () => IsDragging(serverItem) &&
                  serverItem.OwnerClientId != NetworkManager.ServerClientId &&
                  playerA.Orchestrator.States.IsDragging !=
                  playerB.Orchestrator.States.IsDragging,
            "Concurrent drag requests did not resolve to exactly one winner.");

        ulong winnerClientId = serverItem.OwnerClientId;
        bool clientAWon = winnerClientId == clientA.Manager.LocalClientId;
        Assert.That(
            winnerClientId == clientA.Manager.LocalClientId ||
            winnerClientId == clientB.Manager.LocalClientId,
            Is.True);

        NetworkTestPlayer winner = clientAWon ? playerA : playerB;
        NetworkTestPlayer loser = clientAWon ? playerB : playerA;
        Endpoint winnerEndpoint = clientAWon ? clientA : clientB;
        Endpoint remainingEndpoint = clientAWon ? clientB : clientA;
        NetworkItemTestDraggable remainingItem =
            clientAWon ? itemB : itemA;
        Rigidbody remainingBody = remainingItem.GetComponent<Rigidbody>();

        Assert.That(winner.Orchestrator.States.IsDragging, Is.True);
        Assert.That(loser.Orchestrator.States.IsDragging, Is.False);
        Assert.That(winner.Controller.GetSpeed(), Is.LessThan(InitialPlayerSpeed));
        Assert.That(loser.Controller.GetSpeed(), Is.EqualTo(InitialPlayerSpeed));
        Assert.That(
            PlayModeTestReflection.GetField<bool>(
                loser.Interaction,
                "dragRequestPending"),
            Is.False);

        winnerEndpoint.Manager.Shutdown(discardMessageQueue: false);

        yield return WaitForCondition(
            () => !winnerEndpoint.Manager.IsListening &&
                  serverItem.OwnerClientId == NetworkManager.ServerClientId &&
                  !IsDragging(serverItem) &&
                  remainingItem.OwnerClientId ==
                      NetworkManager.ServerClientId &&
                  !IsDragging(remainingItem) &&
                  remainingBody.isKinematic &&
                  !DraggableObject.ActiveDraggedObjects.Contains(
                      remainingItem),
            "Owner disconnect cleanup did not reach the server and remaining client.");

        Assert.That(
            GetSpawnedComponent<NetworkItemTestDraggable>(
                remainingEndpoint,
                networkObjectId),
            Is.SameAs(remainingItem));
        Rigidbody serverBody = serverItem.GetComponent<Rigidbody>();

        Assert.That(serverBody.isKinematic, Is.False);
        Assert.That(serverBody.useGravity, Is.True);
        Assert.That(serverBody.mass, Is.EqualTo(ItemMass).Within(0.001f));
        Assert.That(remainingBody.isKinematic, Is.True);
        Assert.That(
            DraggableObject.ActiveDraggedObjects.Contains(remainingItem),
            Is.False);
        Assert.That(loser.Controller.GetSpeed(), Is.EqualTo(InitialPlayerSpeed));
    }

    private IEnumerator StartNetwork()
    {
        CreateNetworkPrefabs();
        server = CreateEndpoint("Item dedicated server");
        clientA = CreateEndpoint("Item client A");
        clientB = CreateEndpoint("Item client B");
        RegisterPrefabs(server, clientA, clientB);

        Assert.That(server.Manager.StartServer(), Is.True);

        yield return WaitForCondition(
            () => server.Manager.IsServer &&
                  server.Transport.GetLocalEndpoint().Port != 0,
            "Dedicated item test server did not start.");

        ushort port = server.Transport.GetLocalEndpoint().Port;
        clientA.Transport.SetConnectionData("127.0.0.1", port);
        clientB.Transport.SetConnectionData("127.0.0.1", port);

        Assert.That(clientA.Manager.StartClient(), Is.True);
        Assert.That(clientB.Manager.StartClient(), Is.True);

        yield return WaitForCondition(
            () => clientA.Manager.IsConnectedClient &&
                  clientB.Manager.IsConnectedClient &&
                  server.Manager.ConnectedClientsIds.Count == 2,
            "Both item test clients did not connect.");

        SpawnNetworkTestPlayer(clientA.Manager.LocalClientId);
        SpawnNetworkTestPlayer(clientB.Manager.LocalClientId);

        yield return WaitForCondition(
            () => HasPlayerObject(server, clientA.Manager.LocalClientId) &&
                  HasPlayerObject(server, clientB.Manager.LocalClientId) &&
                  clientA.Manager.LocalClient?.PlayerObject != null &&
                  clientB.Manager.LocalClient?.PlayerObject != null,
            "Network test player objects were not spawned on every endpoint.");
    }

    private void CreateNetworkPrefabs()
    {
        networkTestPlayerPrefab = Track(
            new GameObject("Network test player prefab"));
        networkTestPlayerPrefab.SetActive(false);
        NetworkObject playerNetworkObject =
            networkTestPlayerPrefab.AddComponent<NetworkObject>();
        ConfigureNetworkObject(playerNetworkObject, PlayerPrefabHash);

        Rigidbody playerBody = networkTestPlayerPrefab.AddComponent<Rigidbody>();
        playerBody.isKinematic = true;

        PlayerController playerController =
            networkTestPlayerPrefab.AddComponent<PlayerController>();
        playerController.enabled = false;
        playerController.SetSpeed(InitialPlayerSpeed);

        PlayerActionGate actionGate =
            networkTestPlayerPrefab.AddComponent<PlayerActionGate>();
        Transform rayOrigin =
            CreateChild(networkTestPlayerPrefab.transform, "Ray origin");
        Transform camera =
            CreateChild(networkTestPlayerPrefab.transform, "Camera");
        Transform playerViewModel =
            CreateChild(networkTestPlayerPrefab.transform, "View model");
        Transform dropPoint =
            CreateChild(networkTestPlayerPrefab.transform, "Drop point");
        CreateChild(networkTestPlayerPrefab.transform, "Interaction point");

        PlayerInteraction interaction =
            networkTestPlayerPrefab.AddComponent<PlayerInteraction>();
        PlayModeTestReflection.SetField(interaction, "rayOrigin", rayOrigin);
        PlayModeTestReflection.SetField(
            interaction,
            "playerCameraTransform",
            camera);
        PlayModeTestReflection.SetField(
            interaction,
            "playerController",
            playerController);
        PlayModeTestReflection.SetField(
            interaction,
            "viewModelContainer",
            playerViewModel);
        PlayModeTestReflection.SetField(
            interaction,
            "itemDropTransform",
            dropPoint);
        PlayModeTestReflection.SetField(
            interaction,
            "playerActionGateSource",
            actionGate);

        networkTestPlayerPrefab.AddComponent<PlayerOrchestrator>();
        networkTestPlayerPrefab.SetActive(true);

        DraggableObjectData draggableData =
            Track(ScriptableObject.CreateInstance<DraggableObjectData>());
        ConfigureItemData(draggableData);
        draggablePrefab = CreateNetworkItemPrefab<NetworkItemTestDraggable>(
            "Network draggable test prefab",
            DraggablePrefabHash,
            draggableData,
            model: null);

        PickupItemData pickupData =
            Track(ScriptableObject.CreateInstance<PickupItemData>());
        ConfigureItemData(pickupData);
        pickupData.ItemID = 7001;
        GameObject viewModel = Track(new GameObject("Pickup view model"));
        viewModel.SetActive(false);
        pickupPrefab = CreateNetworkItemPrefab<NetworkItemTestPickup>(
            "Network pickup test prefab",
            PickupPrefabHash,
            pickupData,
            viewModel);
    }

    private GameObject CreateNetworkItemPrefab<T>(
        string name,
        uint hash,
        DraggableObjectData data,
        GameObject model)
        where T : DraggableObject
    {
        GameObject prefab = Track(new GameObject(name));
        prefab.SetActive(false);
        prefab.transform.position = new Vector3(10000f, 10000f, 10000f);

        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        ConfigureNetworkObject(networkObject, hash);

        Rigidbody body = prefab.AddComponent<Rigidbody>();
        body.mass = ItemMass;
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        NetworkTransform networkTransform = prefab.AddComponent<NetworkTransform>();
        networkTransform.AuthorityMode =
            NetworkTransform.AuthorityModes.Owner;
        prefab.AddComponent<NetworkRigidbody>();
        prefab.AddComponent<BoxCollider>();

        T item = prefab.AddComponent<T>();
        PlayModeTestReflection.SetField(item, "data", data);

        if (item is PickupItem)
            PlayModeTestReflection.SetField(item, "model", model);

        prefab.SetActive(true);
        return prefab;
    }

    private static void ConfigureNetworkObject(
        NetworkObject networkObject,
        uint hash)
    {
        PlayModeTestReflection.SetField(
            networkObject,
            "GlobalObjectIdHash",
            hash);
        PropertyInfo sceneObjectProperty = typeof(NetworkObject).GetProperty(
            nameof(NetworkObject.IsSceneObject),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(sceneObjectProperty, Is.Not.Null);
        sceneObjectProperty.SetValue(networkObject, false);
    }

    private static void ConfigureItemData(DraggableObjectData data)
    {
        data.BlocksEnemyNavigation = true;
        data.Mass = ItemMass;
        data.MaxFollowSpeed = 15f;
        data.FollowSpeedMultiplier = 2f;
        data.MaxDragDistance = 20f;
        data.ThrowVelocitySamples = 3f;
        data.MinDistance = 0f;
    }

    private void RegisterPrefabs(params Endpoint[] targets)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            targets[i].Manager.NetworkConfig.Prefabs.Add(
                new NetworkPrefab { Prefab = networkTestPlayerPrefab });
            targets[i].Manager.NetworkConfig.Prefabs.Add(
                new NetworkPrefab { Prefab = draggablePrefab });
            targets[i].Manager.NetworkConfig.Prefabs.Add(
                new NetworkPrefab { Prefab = pickupPrefab });
        }
    }

    private ulong SpawnOnServer<T>(GameObject prefab, Vector3 position)
        where T : NetworkBehaviour
    {
        GameObject instance = Object.Instantiate(
            prefab,
            position,
            Quaternion.identity);
        instance.name = $"{typeof(T).Name} spawned";
        Track(instance);

        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        PlayModeTestReflection.SetField(
            networkObject,
            "NetworkManagerOwner",
            server.Manager);
        networkObject.Spawn();
        Assert.That(networkObject.IsSpawned, Is.True);
        return networkObject.NetworkObjectId;
    }

    private IEnumerator WaitForSpawnOnEveryEndpoint(ulong networkObjectId)
    {
        yield return WaitForCondition(
            () => HasSpawnedObject(server, networkObjectId) &&
                  HasSpawnedObject(clientA, networkObjectId) &&
                  HasSpawnedObject(clientB, networkObjectId),
            $"Network item {networkObjectId} was not spawned on every endpoint.");
    }

    private static bool HasSpawnedObject(Endpoint endpoint, ulong networkObjectId)
    {
        return endpoint?.Manager?.SpawnManager != null &&
               endpoint.Manager.SpawnManager.SpawnedObjects.ContainsKey(
                   networkObjectId);
    }

    private static T GetSpawnedComponent<T>(
        Endpoint endpoint,
        ulong networkObjectId)
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

    private static void BeginDragRequest(
        NetworkItemTestDraggable item,
        NetworkTestPlayer player,
        Vector3 hitPosition)
    {
        Assert.That(
            player.ActionGate.TryBegin(PlayerActionKind.Drag, item),
            Is.True);
        PlayModeTestReflection.SetField(
            player.Interaction,
            "dragRequestPending",
            true);
        PlayModeTestReflection.SetField(
            player.Interaction,
            "pendingDraggable",
            item);
        item.OnInteract(player.CreateInteractionContext(hitPosition));
    }

    private static void BeginPickupRequest(
        NetworkItemTestPickup item,
        NetworkTestPlayer player)
    {
        Assert.That(
            player.ActionGate.TryBegin(PlayerActionKind.Pickup, item),
            Is.True);
        PlayModeTestReflection.SetField(
            player.Interaction,
            "pickupRequestPending",
            true);
        PlayModeTestReflection.SetField(
            player.Interaction,
            "pendingPickup",
            item);
        item.OnPickup(player.CreatePickupContext());
    }

    private static NetworkTestPlayer GetNetworkTestPlayer(Endpoint endpoint)
    {
        NetworkObject playerObject = endpoint.Manager.LocalClient.PlayerObject;
        Assert.That(playerObject, Is.Not.Null);

        PlayerOrchestrator orchestrator =
            playerObject.GetComponent<PlayerOrchestrator>();
        PlayerInteraction interaction =
            playerObject.GetComponent<PlayerInteraction>();
        PlayerActionGate actionGate =
            playerObject.GetComponent<PlayerActionGate>();
        PlayerController controller =
            playerObject.GetComponent<PlayerController>();

        Assert.That(orchestrator, Is.Not.Null);
        Assert.That(interaction, Is.Not.Null);
        Assert.That(actionGate, Is.Not.Null);
        Assert.That(controller, Is.Not.Null);

        orchestrator.Setup(isMultiplayer: true, isOwner: true);

        Transform root = playerObject.transform;
        return new NetworkTestPlayer(
            orchestrator,
            interaction,
            actionGate,
            controller,
            root.Find("Camera"),
            root.Find("View model"),
            root.Find("Drop point"),
            root.Find("Interaction point"));
    }

    private static Transform CreateChild(Transform parent, string name)
    {
        GameObject child = new(name);
        child.transform.SetParent(parent, false);
        return child.transform;
    }

    private void SpawnNetworkTestPlayer(ulong ownerClientId)
    {
        GameObject instance = Object.Instantiate(networkTestPlayerPrefab);
        Track(instance);
        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        PlayModeTestReflection.SetField(
            networkObject,
            "NetworkManagerOwner",
            server.Manager);
        networkObject.SpawnAsPlayerObject(ownerClientId);
    }

    private static bool HasPlayerObject(
        Endpoint endpoint,
        ulong clientId)
    {
        return endpoint.Manager.ConnectedClients.TryGetValue(
                   clientId,
                   out NetworkClient client) &&
               client.PlayerObject != null;
    }

    private void AssertServerCanReach(
        ulong clientId,
        DraggableObject target)
    {
        PlayerInteraction interaction = server.Manager.ConnectedClients[clientId]
            .PlayerObject.GetComponent<PlayerInteraction>();
        Transform origin = PlayModeTestReflection.GetField<Transform>(
            interaction,
            "rayOrigin");
        bool canReach = (bool)PlayModeTestReflection.Invoke(
            interaction,
            "CanReach",
            target);

        Assert.That(
            canReach,
            Is.True,
            $"Server player {clientId} at {origin.position} cannot reach " +
            $"{target.name} at {target.transform.position}.");
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
                (manager.IsListening ||
                 manager.IsClient ||
                 manager.IsServer ||
                 manager.ShutdownInProgress))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDragging(DraggableObject item)
    {
        return PlayModeTestReflection
            .GetField<NetworkVariable<bool>>(item, "netIsDragging")
            .Value;
    }

    private static bool IsPickedUp(PickupItem item)
    {
        return PlayModeTestReflection
            .GetField<NetworkVariable<bool>>(item, "netIsPickedUp")
            .Value;
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

    private sealed class NetworkTestPlayer
    {
        internal NetworkTestPlayer(
            PlayerOrchestrator orchestrator,
            PlayerInteraction interaction,
            PlayerActionGate actionGate,
            PlayerController controller,
            Transform camera,
            Transform viewModel,
            Transform dropPoint,
            Transform interactionPoint)
        {
            Orchestrator = orchestrator;
            Interaction = interaction;
            ActionGate = actionGate;
            Controller = controller;
            Camera = camera;
            ViewModel = viewModel;
            DropPoint = dropPoint;
            InteractionPoint = interactionPoint;
        }

        internal PlayerOrchestrator Orchestrator { get; }
        internal PlayerInteraction Interaction { get; }
        internal PlayerActionGate ActionGate { get; }
        internal PlayerController Controller { get; }
        internal Transform Camera { get; }
        internal Transform ViewModel { get; }
        internal Transform DropPoint { get; }
        internal Transform InteractionPoint { get; }

        internal InteractionContext CreateInteractionContext(Vector3 hitPosition)
        {
            InteractionPoint.position = hitPosition;
            return new InteractionContext
            {
                HitPoint = InteractionPoint,
                PlayerCameraTransform = Camera,
                PlayerController = Controller,
                PlayerActionGate = ActionGate,
                RayOriginPosition = Camera.position,
                PlayerInteraction = Interaction
            };
        }

        internal PickUpContext CreatePickupContext()
        {
            return new PickUpContext
            {
                ViewModelContainer = ViewModel,
                OwnerTransform = DropPoint,
                PlayerInteraction = Interaction
            };
        }
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
                ProtocolVersion = 4
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
