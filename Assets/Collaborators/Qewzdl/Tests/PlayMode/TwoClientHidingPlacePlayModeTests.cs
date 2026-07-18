using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

[Category("Multiplayer")]
public sealed class TwoClientHidingPlacePlayModeTests
{
    private const float TimeoutSeconds = 10f;
    private const uint PlayerPrefabHash = 0x17A60011u;
    private const uint HidingPlacePrefabHash = 0x17A60012u;

    private readonly List<Endpoint> endpoints = new();
    private readonly List<Object> cleanup = new();

    private Endpoint server;
    private Endpoint clientA;
    private Endpoint clientB;
    private GameObject playerPrefab;
    private GameObject hidingPlacePrefab;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        for (int i = 0; i < endpoints.Count; i++)
        {
            NetworkManager manager = endpoints[i].Manager;

            if (manager != null && manager.IsListening)
            {
                manager.Shutdown(discardMessageQueue: true);
            }
        }

        float timeout = Time.realtimeSinceStartup + TimeoutSeconds;

        while (!AllEndpointsStopped() &&
               Time.realtimeSinceStartup < timeout)
        {
            yield return null;
        }

        for (int i = endpoints.Count - 1; i >= 0; i--)
        {
            endpoints[i].Dispose();
        }

        endpoints.Clear();

        for (int i = cleanup.Count - 1; i >= 0; i--)
        {
            if (cleanup[i] != null)
            {
                Object.DestroyImmediate(cleanup[i]);
            }
        }

        cleanup.Clear();
        server = null;
        clientA = null;
        clientB = null;
        playerPrefab = null;
        hidingPlacePrefab = null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator ConcurrentEntry_HasOneWinner_ExitRestoresPlayer()
    {
        yield return StartNetwork();

        ulong playerAId = SpawnPlayer(clientA.Manager.LocalClientId);
        ulong playerBId = SpawnPlayer(clientB.Manager.LocalClientId);
        ulong hidingPlaceId = SpawnHidingPlace();

        yield return WaitForSpawnOnEveryEndpoint(
            playerAId,
            playerBId,
            hidingPlaceId
        );

        PlayerHidingController playerA = GetComponent<PlayerHidingController>(
            clientA,
            playerAId
        );
        PlayerHidingController playerB = GetComponent<PlayerHidingController>(
            clientB,
            playerBId
        );
        HidingPlaceInteractable placeA =
            GetComponent<HidingPlaceInteractable>(
                clientA,
                hidingPlaceId
            );
        HidingPlaceInteractable placeB =
            GetComponent<HidingPlaceInteractable>(
                clientB,
                hidingPlaceId
            );
        HidingPlaceInteractable serverPlace =
            GetComponent<HidingPlaceInteractable>(
                server,
                hidingPlaceId
            );

        Assert.That(placeA.TryRequestEnter(playerA), Is.True);
        Assert.That(placeB.TryRequestEnter(playerB), Is.True);

        yield return WaitForCondition(
            () =>
            {
                PlayerHidingController serverPlayerA =
                    GetComponent<PlayerHidingController>(
                        server,
                        playerAId
                    );
                PlayerHidingController serverPlayerB =
                    GetComponent<PlayerHidingController>(
                        server,
                        playerBId
                    );

                return serverPlace.IsOccupied &&
                       serverPlayerA.IsHidden != serverPlayerB.IsHidden &&
                       playerA.IsHidden != playerB.IsHidden &&
                       (serverPlace.OccupantNetworkObjectId == playerAId
                           ? playerA.IsHidden
                           : playerB.IsHidden);
            },
            "Concurrent hiding requests did not produce exactly one occupant."
        );

        bool clientAWon =
            serverPlace.OccupantNetworkObjectId == playerAId;
        PlayerHidingController winner = clientAWon ? playerA : playerB;
        PlayerHidingController loser = clientAWon ? playerB : playerA;
        ulong winnerId = clientAWon ? playerAId : playerBId;

        Assert.That(winner.IsHidden, Is.True);
        Assert.That(loser.IsHidden, Is.False);
        Assert.That(
            winner.HidingPlaceNetworkObjectId,
            Is.EqualTo(hidingPlaceId)
        );

        Rigidbody winnerBody = winner.GetComponent<Rigidbody>();
        Collider winnerCollider = winner.GetComponent<Collider>();
        Renderer winnerRenderer = winner.GetComponent<Renderer>();
        PlayerController winnerMovement =
            winner.GetComponent<PlayerController>();

        Assert.That(
            winnerBody.constraints,
            Is.EqualTo(RigidbodyConstraints.FreezeAll)
        );
        Assert.That(winnerCollider.enabled, Is.False);
        Assert.That(winnerRenderer.enabled, Is.False);
        Assert.That(winnerMovement.IsMovementActive, Is.False);

        winner.RequestExitHiding();

        yield return WaitForCondition(
            () => !serverPlace.IsOccupied &&
                  !GetComponent<PlayerHidingController>(
                      server,
                      winnerId
                  ).IsHidden &&
                  !winner.IsHidden,
            "Hiding exit did not clear occupancy and replicated player state."
        );

        Assert.That(
            winnerBody.constraints,
            Is.EqualTo(RigidbodyConstraints.None)
        );
        Assert.That(winnerCollider.enabled, Is.True);
        Assert.That(winnerRenderer.enabled, Is.True);
        Assert.That(winnerMovement.IsMovementActive, Is.True);
    }

    [UnityTest]
    public IEnumerator OccupantDisconnect_ReleasesPlace_ForRemainingClient()
    {
        yield return StartNetwork();

        ulong playerAId = SpawnPlayer(clientA.Manager.LocalClientId);
        ulong playerBId = SpawnPlayer(clientB.Manager.LocalClientId);
        ulong hidingPlaceId = SpawnHidingPlace();

        yield return WaitForSpawnOnEveryEndpoint(
            playerAId,
            playerBId,
            hidingPlaceId
        );

        PlayerHidingController playerA = GetComponent<PlayerHidingController>(
            clientA,
            playerAId
        );
        HidingPlaceInteractable placeA =
            GetComponent<HidingPlaceInteractable>(
                clientA,
                hidingPlaceId
            );
        HidingPlaceInteractable serverPlace =
            GetComponent<HidingPlaceInteractable>(
                server,
                hidingPlaceId
            );

        Assert.That(placeA.TryRequestEnter(playerA), Is.True);

        yield return WaitForCondition(
            () => serverPlace.OccupantNetworkObjectId == playerAId,
            "Client A did not occupy the hiding place."
        );

        clientA.Manager.Shutdown(discardMessageQueue: false);

        yield return WaitForCondition(
            () => !clientA.Manager.IsListening &&
                  !server.Manager.SpawnManager.SpawnedObjects.ContainsKey(
                      playerAId
                  ) &&
                  !serverPlace.IsOccupied,
            "Disconnect did not despawn the occupant and release the place."
        );

        PlayerHidingController playerB = GetComponent<PlayerHidingController>(
            clientB,
            playerBId
        );
        HidingPlaceInteractable placeB =
            GetComponent<HidingPlaceInteractable>(
                clientB,
                hidingPlaceId
            );

        Assert.That(placeB.TryRequestEnter(playerB), Is.True);

        yield return WaitForCondition(
            () => serverPlace.OccupantNetworkObjectId == playerBId &&
                  playerB.IsHidden,
            "Remaining client could not occupy the released hiding place."
        );

        serverPlace.NetworkObject.Despawn(destroy: true);

        yield return WaitForCondition(
            () => !server.Manager.SpawnManager.SpawnedObjects.ContainsKey(
                      hidingPlaceId
                  ) &&
                  !playerB.IsHidden,
            "Hiding place despawn did not release its active occupant."
        );
    }

    private IEnumerator StartNetwork()
    {
        CreateNetworkPrefabs();

        server = CreateEndpoint("Hiding dedicated server");
        clientA = CreateEndpoint("Hiding client A");
        clientB = CreateEndpoint("Hiding client B");

        RegisterPrefabs(server, clientA, clientB);

        Assert.That(server.Manager.StartServer(), Is.True);

        yield return WaitForCondition(
            () => server.Manager.IsServer &&
                  server.Transport.GetLocalEndpoint().Port != 0,
            "Dedicated hiding test server did not start."
        );

        ushort port = server.Transport.GetLocalEndpoint().Port;
        clientA.Transport.SetConnectionData("127.0.0.1", port);
        clientB.Transport.SetConnectionData("127.0.0.1", port);

        Assert.That(clientA.Manager.StartClient(), Is.True);
        Assert.That(clientB.Manager.StartClient(), Is.True);

        yield return WaitForCondition(
            () => clientA.Manager.IsConnectedClient &&
                  clientB.Manager.IsConnectedClient &&
                  server.Manager.ConnectedClientsIds.Count == 2,
            "Both hiding test clients did not connect."
        );
    }

    private void CreateNetworkPrefabs()
    {
        playerPrefab = Track(new GameObject("Hiding player prefab"));
        playerPrefab.SetActive(false);
        playerPrefab.transform.position =
            new Vector3(10000f, 10000f, 10000f);

        NetworkObject playerNetworkObject =
            playerPrefab.AddComponent<NetworkObject>();
        ConfigureNetworkPrefab(
            playerNetworkObject,
            PlayerPrefabHash
        );

        NetworkTransform playerNetworkTransform =
            playerPrefab.AddComponent<NetworkTransform>();
        playerNetworkTransform.AuthorityMode =
            NetworkTransform.AuthorityModes.Owner;

        Rigidbody body = playerPrefab.AddComponent<Rigidbody>();
        body.useGravity = false;
        playerPrefab.AddComponent<CapsuleCollider>();
        playerPrefab.AddComponent<MeshRenderer>();

        PlayerController movement =
            playerPrefab.AddComponent<PlayerController>();
        movement.enabled = false;

        PlayerHidingController hiding =
            playerPrefab.AddComponent<PlayerHidingController>();
        PlayModeTestReflection.SetField(
            hiding,
            "networkTransform",
            playerNetworkTransform
        );
        PlayModeTestReflection.SetField(hiding, "playerBody", body);
        PlayModeTestReflection.SetField(
            hiding,
            "playerController",
            movement
        );

        playerPrefab.SetActive(true);

        HidingPlaceData hidingData =
            Track(ScriptableObject.CreateInstance<HidingPlaceData>());

        hidingPlacePrefab = Track(
            new GameObject("Hiding place prefab")
        );
        hidingPlacePrefab.SetActive(false);
        hidingPlacePrefab.transform.position =
            new Vector3(10000f, 10000f, 10000f);

        NetworkObject placeNetworkObject =
            hidingPlacePrefab.AddComponent<NetworkObject>();
        ConfigureNetworkPrefab(
            placeNetworkObject,
            HidingPlacePrefabHash
        );
        hidingPlacePrefab.AddComponent<BoxCollider>();

        Transform hidingPoint = CreateChild(
            hidingPlacePrefab.transform,
            "Hiding Point",
            Vector3.forward
        );
        Transform exitPoint = CreateChild(
            hidingPlacePrefab.transform,
            "Exit Point",
            Vector3.back
        );

        HidingPlaceInteractable hidingPlace =
            hidingPlacePrefab.AddComponent<HidingPlaceInteractable>();
        PlayModeTestReflection.SetField(hidingPlace, "data", hidingData);
        PlayModeTestReflection.SetField(
            hidingPlace,
            "interactionAnchor",
            hidingPlacePrefab.transform
        );
        PlayModeTestReflection.SetField(
            hidingPlace,
            "hidingPoint",
            hidingPoint
        );
        PlayModeTestReflection.SetField(
            hidingPlace,
            "exitPoint",
            exitPoint
        );

        hidingPlacePrefab.SetActive(true);
    }

    private static void ConfigureNetworkPrefab(
        NetworkObject networkObject,
        uint hash
    )
    {
        PlayModeTestReflection.SetField(
            networkObject,
            "GlobalObjectIdHash",
            hash
        );

        PropertyInfo sceneObjectProperty = typeof(NetworkObject).GetProperty(
            nameof(NetworkObject.IsSceneObject),
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic
        );

        Assert.That(sceneObjectProperty, Is.Not.Null);
        sceneObjectProperty.SetValue(networkObject, false);
    }

    private void RegisterPrefabs(params Endpoint[] targets)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            targets[i].Manager.NetworkConfig.Prefabs.Add(
                new NetworkPrefab { Prefab = playerPrefab }
            );
            targets[i].Manager.NetworkConfig.Prefabs.Add(
                new NetworkPrefab { Prefab = hidingPlacePrefab }
            );
        }
    }

    private ulong SpawnPlayer(ulong ownerClientId)
    {
        GameObject instance = Object.Instantiate(
            playerPrefab,
            Vector3.zero,
            Quaternion.identity
        );
        Track(instance);

        NetworkObject networkObject =
            instance.GetComponent<NetworkObject>();
        PlayModeTestReflection.SetField(
            networkObject,
            "NetworkManagerOwner",
            server.Manager
        );
        networkObject.SpawnWithOwnership(ownerClientId);
        return networkObject.NetworkObjectId;
    }

    private ulong SpawnHidingPlace()
    {
        GameObject instance = Object.Instantiate(
            hidingPlacePrefab,
            Vector3.zero,
            Quaternion.identity
        );
        Track(instance);

        NetworkObject networkObject =
            instance.GetComponent<NetworkObject>();
        PlayModeTestReflection.SetField(
            networkObject,
            "NetworkManagerOwner",
            server.Manager
        );
        networkObject.Spawn();
        return networkObject.NetworkObjectId;
    }

    private IEnumerator WaitForSpawnOnEveryEndpoint(
        params ulong[] networkObjectIds
    )
    {
        yield return WaitForCondition(
            () =>
            {
                for (int i = 0; i < networkObjectIds.Length; i++)
                {
                    if (!HasSpawnedObject(server, networkObjectIds[i]) ||
                        !HasSpawnedObject(clientA, networkObjectIds[i]) ||
                        !HasSpawnedObject(clientB, networkObjectIds[i]))
                    {
                        return false;
                    }
                }

                return true;
            },
            "Hiding objects were not spawned on every endpoint."
        );
    }

    private static bool HasSpawnedObject(
        Endpoint endpoint,
        ulong networkObjectId
    )
    {
        return endpoint?.Manager?.SpawnManager != null &&
               endpoint.Manager.SpawnManager.SpawnedObjects.ContainsKey(
                   networkObjectId
               );
    }

    private static T GetComponent<T>(
        Endpoint endpoint,
        ulong networkObjectId
    )
        where T : Component
    {
        Assert.That(
            endpoint.Manager.SpawnManager.SpawnedObjects.TryGetValue(
                networkObjectId,
                out NetworkObject networkObject
            ),
            Is.True
        );

        T component = networkObject.GetComponent<T>();
        Assert.That(component, Is.Not.Null);
        return component;
    }

    private static Transform CreateChild(
        Transform parent,
        string name,
        Vector3 localPosition
    )
    {
        GameObject child = new(name);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPosition;
        return child.transform;
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

    private static IEnumerator WaitForCondition(
        Func<bool> condition,
        string failureMessage
    )
    {
        float timeout = Time.realtimeSinceStartup + TimeoutSeconds;

        while (!condition.Invoke() &&
               Time.realtimeSinceStartup < timeout)
        {
            yield return null;
        }

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
            UnityTransport transport
        )
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
            UnityTransport transport =
                root.AddComponent<UnityTransport>();
            NetworkManager manager =
                root.AddComponent<NetworkManager>();

            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
                ProtocolVersion = 6
            };

            transport.SetConnectionData(
                "127.0.0.1",
                0,
                "127.0.0.1"
            );

            return new Endpoint(root, manager, transport);
        }

        public void Dispose()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
