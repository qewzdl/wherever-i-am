using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Object = UnityEngine.Object;

// Several machines - a server and its clients, or a host and a guest - in one
// process, for the tests that need more than one.
//
// Every one of these tests had its own copy of this, and every copy met the
// same trouble on its own: the machines share one physics scene, so each
// networked object is there once per machine, and the copies shoved each
// other. An item was pushed out of reach, a player out of range of a hiding
// place, and each suite answered with a patch of its own - a pair of copies
// told to ignore each other here, the whole player layer switched off there.
// Here, nothing on one machine ever touches anything on another, for every
// suite, without asking.
public abstract class MultiEndpointPlayModeTest
{
    protected const float TimeoutSeconds = 10f;

    protected readonly List<Endpoint> endpoints = new();
    protected readonly List<Object> cleanup = new();

    protected Endpoint CreateEndpoint(string name)
    {
        Endpoint endpoint = Endpoint.Create(name);
        endpoints.Add(endpoint);
        EndpointPhysicsSeparation.Ensure(endpoints);
        return endpoint;
    }

    // Everything a test built, gone: the machines shut down and waited for,
    // then removed, then whatever the test asked to have tracked.
    protected IEnumerator StopEndpoints()
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

        EndpointPhysicsSeparation.Remove();

        for (int i = endpoints.Count - 1; i >= 0; i--)
            endpoints[i].Dispose();

        endpoints.Clear();

        for (int i = cleanup.Count - 1; i >= 0; i--)
        {
            if (cleanup[i] != null)
                Object.DestroyImmediate(cleanup[i]);
        }

        cleanup.Clear();
    }

    protected bool AllEndpointsStopped()
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

    protected static IEnumerator WaitForCondition(Func<bool> condition, string failureMessage)
    {
        float timeout = Time.realtimeSinceStartup + TimeoutSeconds;

        while (!condition.Invoke() && Time.realtimeSinceStartup < timeout)
            yield return null;

        Assert.That(condition.Invoke(), Is.True, failureMessage);
    }

    protected T Track<T>(T value)
        where T : Object
    {
        cleanup.Add(value);
        return value;
    }

    protected sealed class Endpoint : IDisposable
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

    // Keeps each machine's colliders out of every other machine's, before
    // every physics step. Asked again each step rather than once per object:
    // switching a collider off and on - hiding does - forgets what it was told
    // to ignore.
    //
    // ponytail: collisions only. A raycast or overlap still sees the other
    // machines' copies, and a test whose query crosses one has to arrange for
    // it (wait for a despawn to arrive, leave the layer out of the mask).
    [DefaultExecutionOrder(-10000)]
    private sealed class EndpointPhysicsSeparation : MonoBehaviour
    {
        private static EndpointPhysicsSeparation instance;

        private readonly List<List<Collider>> collidersByEndpoint = new();
        private IReadOnlyList<Endpoint> endpoints;

        internal static void Ensure(IReadOnlyList<Endpoint> endpoints)
        {
            if (instance == null)
            {
                GameObject host = new("Endpoint physics separation");
                instance = host.AddComponent<EndpointPhysicsSeparation>();
            }

            instance.endpoints = endpoints;
        }

        internal static void Remove()
        {
            if (instance != null)
                DestroyImmediate(instance.gameObject);

            instance = null;
        }

        private void FixedUpdate()
        {
            Separate();
        }

        private void Separate()
        {
            while (collidersByEndpoint.Count < endpoints.Count)
                collidersByEndpoint.Add(new List<Collider>());

            for (int i = 0; i < endpoints.Count; i++)
                CollectColliders(endpoints[i], collidersByEndpoint[i]);

            for (int a = 0; a < endpoints.Count; a++)
            {
                for (int b = a + 1; b < endpoints.Count; b++)
                {
                    foreach (Collider first in collidersByEndpoint[a])
                    {
                        foreach (Collider second in collidersByEndpoint[b])
                            Physics.IgnoreCollision(first, second);
                    }
                }
            }
        }

        private static void CollectColliders(Endpoint endpoint, List<Collider> colliders)
        {
            colliders.Clear();
            NetworkManager manager = endpoint.Manager;

            if (manager == null || manager.SpawnManager == null)
                return;

            foreach (NetworkObject networkObject in manager.SpawnManager.SpawnedObjectsList)
            {
                if (networkObject == null)
                    continue;

                foreach (Collider collider in networkObject.GetComponentsInChildren<Collider>())
                {
                    if (collider.enabled)
                        colliders.Add(collider);
                }
            }
        }
    }
}
