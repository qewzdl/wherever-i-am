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

// The same thing, seen from the host and from a guest.
//
// A host is a server and a player in one program, so anything that has to
// reach a guest over the wire - or that only ever runs "on the server" - just
// happens in place for the host, and a bug in it cannot be seen from there.
// Every test here does something to a player or an object and then asks both
// machines about it, and runs once with the host's player on the receiving
// end and once with the guest's: a feature that only works for one of them
// fails here instead of in somebody's match.
[Category("Multiplayer")]
public sealed class HostGuestParityPlayModeTests
{
    private const float TimeoutSeconds = 10f;
    private const uint PlayerPrefabHash = 0x17A70001u;
    private const uint DraggablePrefabHash = 0x17A70002u;

    private readonly List<Endpoint> endpoints = new();
    private readonly List<Object> cleanup = new();

    private Endpoint host;
    private Endpoint guest;
    private GameObject playerPrefab;
    private GameObject draggablePrefab;
    private Vector3 worldGravity;

    [SetUp]
    public void HoldTheWorldStill()
    {
        worldGravity = Physics.gravity;
        Physics.gravity = Vector3.zero;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Physics.gravity = worldGravity;

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
        host = null;
        guest = null;
        yield return null;
    }

    // Caught means gone - from sight, from the world, from the light and from
    // the enemy's ears - on every machine, whoever was caught.
    [UnityTest]
    public IEnumerator CaughtPlayer_LeavesPlayOnHostAndGuestAlike(
        [Values(false, true)] bool catchTheGuest)
    {
        yield return StartNetwork();

        ulong caughtClient = catchTheGuest
            ? guest.Manager.LocalClientId
            : NetworkManager.ServerClientId;
        ulong survivorClient = catchTheGuest
            ? NetworkManager.ServerClientId
            : guest.Manager.LocalClientId;
        ulong caughtId = PlayerObjectId(caughtClient);
        ulong survivorId = PlayerObjectId(survivorClient);

        PlayModeTestReflection.Invoke(
            GetSpawned<PlayerEnemyAttackReceiver>(host, caughtId),
            "EliminateServerOnly");

        yield return WaitForCondition(
            () => GetSpawned<PlayerEnemyAttackReceiver>(host, caughtId).IsEliminated &&
                  GetSpawned<PlayerEnemyAttackReceiver>(guest, caughtId).IsEliminated,
            "The catch did not reach both machines.");

        foreach (Endpoint endpoint in new[] { host, guest })
        {
            string where = endpoint == host ? "host" : "guest";
            GameObject caught = GetSpawned<PlayerEnemyAttackReceiver>(endpoint, caughtId).gameObject;
            GameObject survivor = GetSpawned<PlayerEnemyAttackReceiver>(endpoint, survivorId).gameObject;

            Assert.That(caught.GetComponentInChildren<Renderer>(true).enabled, Is.False,
                $"The caught player's body is still drawn on the {where}.");
            Assert.That(caught.GetComponentInChildren<Light>(true).enabled, Is.False,
                $"The caught player's light is still on on the {where}.");
            Assert.That(caught.GetComponentInChildren<Collider>(true).enabled, Is.False,
                $"The caught player's body still collides on the {where}.");
            Assert.That(caught.GetComponent<EnemyTarget>().CanBeDetected, Is.False,
                $"The enemy can still see the caught player on the {where}.");

            Assert.That(survivor.GetComponentInChildren<Renderer>(true).enabled, Is.True,
                $"The survivor vanished too on the {where}.");
            Assert.That(survivor.GetComponent<EnemyTarget>().CanBeDetected, Is.True,
                $"The survivor stopped being prey on the {where}.");

            // Only the caught player's own machine watches through somebody
            // else's eyes.
            bool isCaughtPlayersMachine = endpoint.Manager.LocalClientId == caughtClient;
            Assert.That(
                caught.GetComponent<PlayerSpectatorView>() != null,
                Is.EqualTo(isCaughtPlayersMachine),
                $"Spectating was set up on the wrong machine ({where}).");
        }

        GameplayNoiseWorldService noise = CreateNoiseService();

        Assert.That(
            noise.TryRaiseNoiseServer(
                Vector3.zero, 5f, 1f, GameplayNoiseSourceType.Player,
                caughtId, caughtClient,
                GetSpawned<PlayerEnemyAttackReceiver>(host, caughtId)),
            Is.False,
            "The enemy can still hear the caught player.");
        Assert.That(
            noise.TryRaiseNoiseServer(
                Vector3.zero, 5f, 1f, GameplayNoiseSourceType.Player,
                survivorId, survivorClient,
                GetSpawned<PlayerEnemyAttackReceiver>(host, survivorId)),
            Is.True,
            "The enemy stopped hearing the survivor.");
    }

    // An item is simulated by whoever is pushing it. A resting item belongs to
    // the host and is kinematic everywhere else, so without this a guest walked
    // into a wall and the item crept along on the host too slowly to make any
    // sound; the host never saw the difference.
    [UnityTest]
    public IEnumerator PushedItem_IsSimulatedByWhoeverPushesIt()
    {
        yield return StartNetwork();

        ulong itemId = SpawnDraggable(new Vector3(0f, 0f, 2f));

        yield return WaitForCondition(
            () => HasSpawned(host, itemId) && HasSpawned(guest, itemId),
            "The item did not spawn on both machines.");

        // Both copies sit in the one physics scene this test has; left to
        // touch, they shove each other out of reach.
        Physics.IgnoreCollision(
            GetSpawned<BoxCollider>(host, itemId),
            GetSpawned<BoxCollider>(guest, itemId));

        NetworkItemTestDraggable hostItem = GetSpawned<NetworkItemTestDraggable>(host, itemId);
        NetworkItemTestDraggable guestItem = GetSpawned<NetworkItemTestDraggable>(guest, itemId);
        ulong guestClient = guest.Manager.LocalClientId;

        guestItem.RequestPushAuthority();

        yield return WaitForCondition(
            () => hostItem.OwnerClientId == guestClient &&
                  !guestItem.GetComponent<Rigidbody>().isKinematic &&
                  hostItem.GetComponent<Rigidbody>().isKinematic,
            "The guest pushed the item but its physics stayed on the host.");

        hostItem.RequestPushAuthority();

        yield return WaitForCondition(
            () => hostItem.OwnerClientId == NetworkManager.ServerClientId &&
                  !hostItem.GetComponent<Rigidbody>().isKinematic &&
                  guestItem.GetComponent<Rigidbody>().isKinematic,
            "The host pushed the item but its physics stayed with the guest.");

        // Not out of the hands of somebody dragging it.
        NetworkVariable<bool> dragging =
            PlayModeTestReflection.GetField<NetworkVariable<bool>>(hostItem, "netIsDragging");
        dragging.Value = true;
        yield return new WaitForSecondsRealtime(0.3f);

        guestItem.RequestPushAuthority();
        yield return new WaitForSecondsRealtime(0.3f);

        Assert.That(hostItem.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId),
            "Pushing took an item out of the hands of the player dragging it.");

        // Nor to anybody standing too far away to be touching it.
        dragging.Value = false;
        hostItem.GetComponent<Rigidbody>().position = new Vector3(0f, 0f, 20f);
        hostItem.transform.position = new Vector3(0f, 0f, 20f);
        yield return new WaitForSecondsRealtime(0.3f);

        guestItem.RequestPushAuthority();
        yield return new WaitForSecondsRealtime(0.3f);

        Assert.That(hostItem.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId),
            "An item was handed to a player nowhere near it.");
    }

    // And once nobody is touching it, the item goes back to the host. Left
    // with whoever last pushed it, it was simulated on their machine for the
    // rest of the match, and froze for everybody whenever that machine did.
    [UnityTest]
    public IEnumerator ItemLeftAlone_GoesBackToTheHost()
    {
        yield return StartNetwork();

        ulong itemId = SpawnDraggable(new Vector3(0f, 0f, 2f));

        yield return WaitForCondition(
            () => HasSpawned(host, itemId) && HasSpawned(guest, itemId),
            "The item did not spawn on both machines.");

        Physics.IgnoreCollision(
            GetSpawned<BoxCollider>(host, itemId),
            GetSpawned<BoxCollider>(guest, itemId));

        NetworkItemTestDraggable hostItem = GetSpawned<NetworkItemTestDraggable>(host, itemId);
        NetworkItemTestDraggable guestItem = GetSpawned<NetworkItemTestDraggable>(guest, itemId);
        ulong guestClient = guest.Manager.LocalClientId;

        guestItem.RequestPushAuthority();

        yield return WaitForCondition(
            () => hostItem.OwnerClientId == guestClient,
            "The guest never got the item.");

        // Still being pushed: it stays with the guest.
        float pushingUntil = Time.realtimeSinceStartup + 3f;

        while (Time.realtimeSinceStartup < pushingUntil)
        {
            guestItem.RequestPushAuthority();
            yield return null;
        }

        Assert.That(hostItem.OwnerClientId, Is.EqualTo(guestClient),
            "The item was taken from a guest who was still pushing it.");

        // Let go of: back to the host.
        yield return WaitForCondition(
            () => hostItem.OwnerClientId == NetworkManager.ServerClientId &&
                  !hostItem.GetComponent<Rigidbody>().isKinematic &&
                  guestItem.GetComponent<Rigidbody>().isKinematic,
            "An item nobody was touching stayed with the guest.");
    }

    // Whoever moves an item hears it hit things, and so does the server - the
    // enemy listens there. The check that decided who reports an impact asked
    // whether this machine was the server rather than whether it was moving
    // the item, so everything a guest carried or pushed into a wall was
    // silent, and only a hit on another moving item made a sound.
    [UnityTest]
    public IEnumerator ItemThrownIntoAWall_IsHeard(
        [Values(false, true)] bool theGuestMovesIt)
    {
        yield return StartNetwork();

        ulong itemId = SpawnDraggable(new Vector3(0f, 0f, 2f));

        yield return WaitForCondition(
            () => HasSpawned(host, itemId) && HasSpawned(guest, itemId),
            "The item did not spawn on both machines.");

        Physics.IgnoreCollision(
            GetSpawned<BoxCollider>(host, itemId),
            GetSpawned<BoxCollider>(guest, itemId));

        NetworkItemTestDraggable hostItem = GetSpawned<NetworkItemTestDraggable>(host, itemId);
        NetworkItemTestDraggable mover = theGuestMovesIt
            ? GetSpawned<NetworkItemTestDraggable>(guest, itemId)
            : hostItem;

        if (theGuestMovesIt)
        {
            mover.RequestPushAuthority();

            yield return WaitForCondition(
                () => hostItem.OwnerClientId == guest.Manager.LocalClientId &&
                      !mover.GetComponent<Rigidbody>().isKinematic,
                "The guest never got the item to move.");
        }

        GameObject wall = Track(new GameObject("Parity wall"));
        wall.transform.position = new Vector3(0f, 0f, 4f);
        wall.AddComponent<BoxCollider>().size = new Vector3(4f, 4f, 0.2f);
        Physics.SyncTransforms();

        int heard = 0;
        hostItem.GetComponent<NetworkItemImpactSoundEmitter>().ServerImpactAccepted +=
            (_, _) => heard++;

        mover.GetComponent<Rigidbody>().linearVelocity = new Vector3(0f, 0f, 5f);

        yield return WaitForCondition(
            () => heard > 0,
            theGuestMovesIt
                ? "An item the guest threw into a wall made no sound."
                : "An item the host threw into a wall made no sound.");
    }

    private IEnumerator StartNetwork()
    {
        CreatePrefabs();
        host = CreateEndpoint("Parity host");
        guest = CreateEndpoint("Parity guest");
        RegisterPrefabs(host, guest);

        Assert.That(host.Manager.StartHost(), Is.True);

        yield return WaitForCondition(
            () => host.Manager.IsHost &&
                  host.Transport.GetLocalEndpoint().Port != 0,
            "The host did not start.");

        guest.Transport.SetConnectionData(
            "127.0.0.1",
            host.Transport.GetLocalEndpoint().Port);
        Assert.That(guest.Manager.StartClient(), Is.True);

        yield return WaitForCondition(
            () => guest.Manager.IsConnectedClient &&
                  host.Manager.ConnectedClientsIds.Count == 2,
            "The guest did not connect.");

        SpawnPlayer(NetworkManager.ServerClientId);
        SpawnPlayer(guest.Manager.LocalClientId);

        yield return WaitForCondition(
            () => host.Manager.ConnectedClientsIds.Count == 2 &&
                  HasPlayer(host, NetworkManager.ServerClientId) &&
                  HasPlayer(host, guest.Manager.LocalClientId) &&
                  guest.Manager.LocalClient?.PlayerObject != null &&
                  HasSpawned(guest, PlayerObjectId(NetworkManager.ServerClientId)),
            "The players did not spawn on both machines.");
    }

    // A stand-in for the player prefab with just the parts being checked: a
    // body that is drawn and collides, the light it carries, something for
    // the enemy to look at, and a camera for a caught player to watch through.
    private void CreatePrefabs()
    {
        playerPrefab = Track(new GameObject("Parity player prefab"));
        playerPrefab.SetActive(false);
        ConfigureNetworkObject(playerPrefab.AddComponent<NetworkObject>(), PlayerPrefabHash);
        playerPrefab.AddComponent<Rigidbody>().isKinematic = true;
        playerPrefab.AddComponent<PlayerEnemyAttackReceiver>();

        GameObject body = new("Body");
        body.transform.SetParent(playerPrefab.transform, false);
        body.AddComponent<MeshFilter>();
        body.AddComponent<MeshRenderer>();
        CapsuleCollider capsule = body.AddComponent<CapsuleCollider>();
        capsule.radius = 0.3f;
        capsule.height = 1.8f;
        capsule.center = new Vector3(0f, 0.9f, 0f);

        GameObject lamp = new("Lamp");
        lamp.transform.SetParent(playerPrefab.transform, false);
        lamp.AddComponent<Light>();

        GameObject eyes = new("Eyes");
        eyes.transform.SetParent(playerPrefab.transform, false);
        eyes.AddComponent<Camera>().enabled = false;

        // Adding one checks its sources before there is any way to give it
        // them, and says so.
        LogAssert.Expect(LogType.Error, new Regex("EnemyTarget has invalid visibility configuration"));
        EnemyTarget target = playerPrefab.AddComponent<EnemyTarget>();
        PlayModeTestReflection.SetField(target, "visibilityPoints", new[] { body.transform });
        playerPrefab.SetActive(true);

        DraggableObjectData data = Track(ScriptableObject.CreateInstance<DraggableObjectData>());
        data.Mass = 2f;
        data.MaxFollowSpeed = 15f;
        data.FollowSpeedMultiplier = 2f;
        data.MaxDragDistance = 20f;
        data.ThrowVelocitySamples = 3f;

        draggablePrefab = Track(new GameObject("Parity draggable prefab"));
        draggablePrefab.SetActive(false);
        draggablePrefab.transform.position = new Vector3(10000f, 10000f, 10000f);
        ConfigureNetworkObject(draggablePrefab.AddComponent<NetworkObject>(), DraggablePrefabHash);
        draggablePrefab.AddComponent<Rigidbody>().mass = data.Mass;
        draggablePrefab.AddComponent<NetworkTransform>().AuthorityMode =
            NetworkTransform.AuthorityModes.Owner;
        draggablePrefab.AddComponent<NetworkRigidbody>();
        draggablePrefab.AddComponent<BoxCollider>();
        PlayModeTestReflection.SetField(
            draggablePrefab.AddComponent<NetworkItemTestDraggable>(),
            "data",
            data);

        ItemImpactSoundProfile impacts = Track(ScriptableObject.CreateInstance<ItemImpactSoundProfile>());
        PlayModeTestReflection.SetField(
            impacts,
            "lightImpactSound",
            Track(ScriptableObject.CreateInstance<SoundEffect>()));
        PlayModeTestReflection.SetField(
            draggablePrefab.AddComponent<NetworkItemImpactSoundEmitter>(),
            "profile",
            impacts);
        draggablePrefab.SetActive(true);
    }

    private GameplayNoiseWorldService CreateNoiseService()
    {
        GameObject root = Track(new GameObject("Parity noise"));
        root.SetActive(false);
        GameplayNoiseWorldService service = root.AddComponent<GameplayNoiseWorldService>();
        PlayModeTestReflection.SetField(service, "networkManager", host.Manager);
        root.SetActive(true);
        Assert.That(service.Construct(host.Manager), Is.True);
        return service;
    }

    private static void ConfigureNetworkObject(NetworkObject networkObject, uint hash)
    {
        PlayModeTestReflection.SetField(networkObject, "GlobalObjectIdHash", hash);
        PropertyInfo sceneObjectProperty = typeof(NetworkObject).GetProperty(
            nameof(NetworkObject.IsSceneObject),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        sceneObjectProperty.SetValue(networkObject, false);
    }

    private void RegisterPrefabs(params Endpoint[] targets)
    {
        foreach (Endpoint target in targets)
        {
            target.Manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = playerPrefab });
            target.Manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = draggablePrefab });
        }
    }

    private void SpawnPlayer(ulong ownerClientId)
    {
        NetworkObject networkObject = Track(Object.Instantiate(playerPrefab))
            .GetComponent<NetworkObject>();
        PlayModeTestReflection.SetField(networkObject, "NetworkManagerOwner", host.Manager);
        networkObject.SpawnAsPlayerObject(ownerClientId);
    }

    private ulong SpawnDraggable(Vector3 position)
    {
        NetworkObject networkObject = Track(
                Object.Instantiate(draggablePrefab, position, Quaternion.identity))
            .GetComponent<NetworkObject>();
        PlayModeTestReflection.SetField(networkObject, "NetworkManagerOwner", host.Manager);
        networkObject.Spawn();
        return networkObject.NetworkObjectId;
    }

    private ulong PlayerObjectId(ulong clientId)
    {
        return host.Manager.ConnectedClients[clientId].PlayerObject.NetworkObjectId;
    }

    private static bool HasPlayer(Endpoint endpoint, ulong clientId)
    {
        return endpoint.Manager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
               client.PlayerObject != null;
    }

    private static bool HasSpawned(Endpoint endpoint, ulong networkObjectId)
    {
        return endpoint.Manager.SpawnManager != null &&
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

    private Endpoint CreateEndpoint(string name)
    {
        Endpoint endpoint = Endpoint.Create(name);
        endpoints.Add(endpoint);
        return endpoint;
    }

    private bool AllEndpointsStopped()
    {
        foreach (Endpoint endpoint in endpoints)
        {
            NetworkManager manager = endpoint.Manager;

            if (manager != null &&
                (manager.IsListening || manager.IsClient || manager.IsServer ||
                 manager.ShutdownInProgress))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerator WaitForCondition(Func<bool> condition, string failureMessage)
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

        private Endpoint(GameObject root, NetworkManager manager, UnityTransport transport)
        {
            this.root = root;
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
