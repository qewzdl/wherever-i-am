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

// Takes a player out of play for the rules that ask, whatever would do it in
// the game - caught today, something else tomorrow. The item rules are
// written against that question, so that is what these tests turn off.
public sealed class InPlayProbe : MonoBehaviour, IPlayerInPlay
{
    public bool IsInPlay { get; set; } = true;
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
    private const uint HandlePrefabHash = 0x17A60004u;
    private const uint EntranceDoorPrefabHash = 0x17A60005u;

    private readonly List<Endpoint> endpoints = new();
    private readonly List<Object> cleanup = new();

    private Endpoint server;
    private Endpoint clientA;
    private Endpoint clientB;
    private GameObject draggablePrefab;
    private GameObject pickupPrefab;
    private GameObject networkTestPlayerPrefab;
    private GameObject handlePrefab;
    private GameObject entranceDoorPrefab;

    private Vector3 worldGravity;

    // The items in this fixture spawn two metres up with nothing underneath
    // them, so gravity does not hold anything against anything here - it only
    // means every position this test reads has been falling since it was set.
    //
    // That is what made the restored-to-spawn assertion flap: the server puts
    // the item back and makes it dynamic in one step, and the distance the
    // test then measures is however many frames passed before the poll noticed
    // - four under no load, a dozen with the whole suite running. Sampling on
    // the first frame the release lands narrowed the window without closing
    // it.
    //
    // The Rigidbody's own useGravity flag is asserted in several places and
    // stays exactly as meaningful: it says what the product does with the
    // component, which is the thing being tested. This says what the world
    // does to it, which is not.
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

        // Where the server last saw client B put things down: at their feet.
        Vector3 carrierFeet = server.Manager
            .ConnectedClients[clientB.Manager.LocalClientId]
            .PlayerObject.GetComponent<PlayerInteraction>()
            .ItemDropPoint.position;

        clientB.Manager.Shutdown(discardMessageQueue: false);

        // Sampled the first frame the release lands, not after the wait
        // returns, so this reads where the server put the item rather than
        // where it had got to by the time the wait noticed.
        //
        // And read off the Rigidbody, which is what the server moves. Its
        // transform only catches up on the next physics step, so a frame with
        // no fixed step in it still showed the item where it had been carried
        // - the player, two metres off. Which frames get a fixed step is down
        // to load, which is why this failed only in the full run; with
        // fixedDeltaTime raised to half a second it failed every time. The
        // transform is still checked, once physics has had a step.
        //
        // What moved it was a carried item being hidden by teleporting the
        // body a thousand metres down, on every instance, including the ones
        // that do not own the position. These items sync through a
        // NetworkTransform with owner authority, so those writes raced the
        // sync and whichever landed last decided where the item was. Hiding
        // turns the renderer and the colliders off now and moves nothing.
        bool hasRestoredPosition = false;
        Vector3 restoredPosition = Vector3.zero;

        yield return WaitForCondition(
            () =>
            {
                // Sampled the frame the server lets go, before the other
                // copies of the item - all in this one physics scene - can
                // shove it. Waiting for client A to hear about it first gave
                // them a few physics steps to do that, and the item was
                // measured a tenth of a metre from where it had been put:
                // the failure this test was known for.
                if (!hasRestoredPosition &&
                    !IsPickedUp(serverItem) &&
                    serverItem.OwnerClientId == NetworkManager.ServerClientId)
                {
                    hasRestoredPosition = true;
                    restoredPosition = serverItem.GetComponent<Rigidbody>().position;
                    IgnoreReplicaCollisions(networkObjectId);
                }

                return hasRestoredPosition &&
                       !clientB.Manager.IsListening &&
                       itemA.OwnerClientId == NetworkManager.ServerClientId;
            },
            "Disconnect did not return the picked item to server ownership.");

        Assert.That(
            Vector3.Distance(restoredPosition, carrierFeet),
            Is.LessThan(0.05f),
            "The server did not put the item down where its carrier left.");

        yield return WaitForCondition(
            () => Vector3.Distance(serverItem.transform.position, carrierFeet) < 0.05f,
            "The item's transform never followed its body to where its carrier left.");
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

    // Carried is gone from the world, all of it, on every player's machine. Only the
    // first mesh used to be hidden, so an item made of several left the rest
    // hanging in the air where it was picked up.
    [UnityTest]
    public IEnumerator CarriedItem_IsHiddenWholeOnEveryMachine()
    {
        yield return StartNetwork();

        ulong networkObjectId = SpawnOnServer<NetworkItemTestPickup>(
            pickupPrefab,
            new Vector3(0f, 2f, 0f));
        yield return WaitForSpawnOnEveryEndpoint(networkObjectId);

        NetworkTestPlayer playerA = GetNetworkTestPlayer(clientA);
        NetworkItemTestPickup itemA =
            GetSpawnedComponent<NetworkItemTestPickup>(clientA, networkObjectId);

        AssertServerCanReach(
            clientA.Manager.LocalClientId,
            GetSpawnedComponent<NetworkItemTestPickup>(server, networkObjectId));
        BeginPickupRequest(itemA, playerA);

        yield return WaitForCondition(
            () => IsDrawnEverywhere(networkObjectId, false),
            "Part of a carried item is still drawn in the world.");

        itemA.OnDrop();

        yield return WaitForCondition(
            () => IsDrawnEverywhere(networkObjectId, true),
            "A dropped item did not come back whole.");
    }

    // A caught player watches through somebody else's eyes, and that means
    // their crosshair too: what it is on is published by the player it
    // belongs to, and every other copy of that player can read it.
    //
    // Driven by player A's own interaction ray, looking at the item and then
    // away: setting the focus by hand raced the ray, which cleared it again
    // on the next frame.
    [UnityTest]
    public IEnumerator Crosshair_IsSeenByAnybodyWatchingThroughTheseEyes()
    {
        yield return StartNetwork();

        NetworkTestPlayer playerA = GetNetworkTestPlayer(clientA);
        ulong playerAId = clientA.Manager.LocalClient.PlayerObject.NetworkObjectId;
        Transform eyes = PlayModeTestReflection.GetField<Transform>(playerA.Interaction, "rayOrigin");
        PlayModeTestReflection.SetField(
            playerA.Interaction,
            "interactableLayer",
            (LayerMask)Physics.DefaultRaycastLayers);

        // In front of A's eyes, and within reach of them.
        ulong itemId = SpawnOnServer<NetworkItemTestDraggable>(
            draggablePrefab,
            eyes.position + Vector3.forward * 1.5f);
        yield return WaitForSpawnOnEveryEndpoint(itemId);
        IgnoreReplicaCollisions(itemId);

        Sprite hand = Track(Sprite.Create(
            Texture2D.whiteTexture,
            new Rect(0f, 0f, 4f, 4f),
            new Vector2(0.5f, 0.5f)));
        PlayModeTestReflection
            .GetField<InteractableObjectData>(
                GetSpawnedComponent<NetworkItemTestDraggable>(server, itemId),
                "data")
            .InteractionSprite = hand;

        PlayerInteraction playerASeenByB = clientB.Manager.SpawnManager
            .SpawnedObjects[playerAId]
            .GetComponent<PlayerInteraction>();

        eyes.rotation = Quaternion.LookRotation(Vector3.forward);

        yield return WaitForCondition(
            () => playerASeenByB.FocusedSprite == hand,
            "Somebody watching player A did not see their crosshair on the item.");

        eyes.rotation = Quaternion.LookRotation(Vector3.back);

        yield return WaitForCondition(
            () => playerASeenByB.FocusedSprite == null,
            "Somebody watching player A kept seeing the item after they looked away.");
    }

    // =====================================================================
    // Letting go of what a player can no longer hold. One rule - a player
    // out of play holds nothing - and every way it can happen: taken out of
    // play while carrying, while dragging, and an item taken off them by the
    // enemy. The disconnect case is PickupDrop above.

    [UnityTest]
    public IEnumerator CarriedItem_IsPutDownAtTheFeetOfACarrierWhoLeavesPlay()
    {
        yield return StartNetwork();

        ulong networkObjectId = SpawnOnServer<NetworkItemTestPickup>(
            pickupPrefab,
            new Vector3(0f, 2f, 0f));
        yield return WaitForSpawnOnEveryEndpoint(networkObjectId);

        NetworkItemTestPickup serverItem =
            GetSpawnedComponent<NetworkItemTestPickup>(server, networkObjectId);
        NetworkItemTestPickup itemA =
            GetSpawnedComponent<NetworkItemTestPickup>(clientA, networkObjectId);
        NetworkTestPlayer playerA = GetNetworkTestPlayer(clientA);

        AssertServerCanReach(clientA.Manager.LocalClientId, serverItem);
        BeginPickupRequest(itemA, playerA);

        yield return WaitForCondition(
            () => IsPickedUp(serverItem) &&
                  itemA.IsOwner &&
                  playerA.Orchestrator.States.IsCarrying,
            "Client A could not pick the item up.");

        NetworkObject carrier = server.Manager
            .ConnectedClients[clientA.Manager.LocalClientId]
            .PlayerObject;
        Vector3 carrierFeet =
            carrier.GetComponent<PlayerInteraction>().ItemDropPoint.position;

        carrier.GetComponent<InPlayProbe>().IsInPlay = false;

        yield return WaitForCondition(
            () => !IsPickedUp(serverItem) &&
                  serverItem.OwnerClientId == NetworkManager.ServerClientId,
            "A carrier taken out of play kept the item.");

        Assert.That(
            Vector3.Distance(serverItem.GetComponent<Rigidbody>().position, carrierFeet),
            Is.LessThan(0.05f),
            "The item was not put down at the feet of its carrier.");
        IgnoreReplicaCollisions(networkObjectId);

        yield return WaitForCondition(
            () => !playerA.Orchestrator.States.IsCarrying &&
                  playerA.ViewModel.childCount == 0 &&
                  IsDrawnEverywhere(networkObjectId, true),
            "The hands of the carrier were not emptied, or the item was not shown again.");

        NetworkTestPlayer playerB = GetNetworkTestPlayer(clientB);
        AssertServerCanReach(clientB.Manager.LocalClientId, serverItem);
        BeginPickupRequest(
            GetSpawnedComponent<NetworkItemTestPickup>(clientB, networkObjectId),
            playerB);

        yield return WaitForCondition(
            () => IsPickedUp(serverItem) &&
                  serverItem.OwnerClientId == clientB.Manager.LocalClientId,
            "Nobody could pick the item up again.");
    }

    [UnityTest]
    public IEnumerator DraggedItem_IsLetGoWhenItsDraggerLeavesPlay()
    {
        yield return DragThenLoseIt(
            serverItem => server.Manager
                .ConnectedClients[clientA.Manager.LocalClientId]
                .PlayerObject.GetComponent<InPlayProbe>().IsInPlay = false,
            "A dragger taken out of play");
    }

    [UnityTest]
    public IEnumerator DraggedItem_TakenByTheEnemy_LetsTheDraggerGo()
    {
        yield return DragThenLoseIt(
            serverItem => serverItem.GetComponent<ItemNavigationObstacle>().RequestPushThrough(),
            "A dragger whose item the enemy pushed through");
    }

    // Client A drags an item, loses it to whatever takeAway does, and must
    // end up with nothing in hand, their speed back, and the item free.
    private IEnumerator DragThenLoseIt(
        Action<NetworkItemTestDraggable> takeAway,
        string who)
    {
        yield return StartNetwork();

        ulong networkObjectId = SpawnOnServer<NetworkItemTestDraggable>(
            draggablePrefab,
            new Vector3(0f, 1f, 0f));
        yield return WaitForSpawnOnEveryEndpoint(networkObjectId);

        NetworkTestPlayer player = GetNetworkTestPlayer(clientA);
        NetworkItemTestDraggable clientItem =
            GetSpawnedComponent<NetworkItemTestDraggable>(clientA, networkObjectId);
        NetworkItemTestDraggable serverItem =
            GetSpawnedComponent<NetworkItemTestDraggable>(server, networkObjectId);

        AssertServerCanReach(clientA.Manager.LocalClientId, serverItem);
        BeginDragRequest(clientItem, player, clientItem.transform.position);

        yield return WaitForCondition(
            () => IsDragging(serverItem) &&
                  player.Orchestrator.States.IsDragging &&
                  player.Controller.GetSpeed() < InitialPlayerSpeed,
            "Client A did not start dragging.");

        takeAway(serverItem);

        yield return WaitForCondition(
            () => !IsDragging(serverItem) &&
                  serverItem.OwnerClientId == NetworkManager.ServerClientId,
            $"{who} kept the item.");

        yield return WaitForCondition(
            () => !player.Orchestrator.States.IsDragging &&
                  Mathf.Approximately(player.Controller.GetSpeed(), InitialPlayerSpeed) &&
                  clientItem.GetComponent<Rigidbody>().useGravity,
            $"{who} was left dragging on their own machine.");
    }

    // The three copies of one item share this fixture's physics scene and,
    // left to touch, shove each other out of reach. Redone after anything
    // that turns the colliders back on, which forgets it.
    private void IgnoreReplicaCollisions(ulong networkObjectId)
    {
        List<Collider> copies = new();

        foreach (Endpoint endpoint in new[] { server, clientA, clientB })
        {
            if (endpoint.Manager.IsListening && HasSpawnedObject(endpoint, networkObjectId))
                copies.Add(GetSpawnedComponent<BoxCollider>(endpoint, networkObjectId));
        }

        for (int i = 0; i < copies.Count; i++)
        {
            for (int j = i + 1; j < copies.Count; j++)
                Physics.IgnoreCollision(copies[i], copies[j]);
        }
    }

    private bool IsDrawnEverywhere(ulong networkObjectId, bool drawn)
    {
        // The clients only: a server that is not also a player draws nothing,
        // and is never sent the order to hide what it does not draw.
        foreach (Endpoint endpoint in new[] { clientA, clientB })
        {
            MeshRenderer[] parts = GetSpawnedComponent<NetworkItemTestPickup>(
                    endpoint,
                    networkObjectId)
                .GetComponentsInChildren<MeshRenderer>(true);

            Assert.That(parts.Length, Is.EqualTo(2));

            foreach (MeshRenderer part in parts)
            {
                if (part.enabled != drawn)
                    return false;
            }
        }

        return true;
    }

    // The one handle in the house can end the match or save it, so the
    // server takes nobody's word about it. It goes into the exit only from
    // somebody standing at the exit, and it is destroyed only by whoever
    // carries it and only once a door has taken it - open to anybody, a
    // single message from any client destroyed it and nobody could win.
    [UnityTest]
    public IEnumerator DoorHandle_IsUsedOnlyAtTheDoorAndDestroyedOnlyOnceUsed()
    {
        yield return StartNetwork();

        ulong handleId = SpawnOnServer<SuperSimpleDoorHandle>(
            handlePrefab,
            new Vector3(0f, 0f, 0.5f));
        ulong nearDoorId = SpawnOnServer<EntranceDoor>(
            entranceDoorPrefab,
            new Vector3(0f, 0f, 2.5f));
        ulong farDoorId = SpawnOnServer<EntranceDoor>(
            entranceDoorPrefab,
            new Vector3(0f, 0f, 20f));
        yield return WaitForSpawnOnEveryEndpoint(handleId);
        yield return WaitForSpawnOnEveryEndpoint(nearDoorId);
        yield return WaitForSpawnOnEveryEndpoint(farDoorId);

        SuperSimpleDoorHandle serverHandle =
            GetSpawnedComponent<SuperSimpleDoorHandle>(server, handleId);
        int itemId = serverHandle.GetItemID();

        // Picked up and thrown away before any door took it: dropped, not
        // destroyed.
        NetworkTestPlayer playerA = GetNetworkTestPlayer(clientA);
        SuperSimpleDoorHandle handleA =
            GetSpawnedComponent<SuperSimpleDoorHandle>(clientA, handleId);
        AssertServerCanReach(clientA.Manager.LocalClientId, serverHandle);
        BeginPickupRequest(handleA, playerA);

        yield return WaitForCondition(
            () => IsPickedUp(serverHandle) &&
                  handleA.IsOwner &&
                  playerA.Orchestrator.States.IsCarrying,
            "Client A could not pick the handle up.");

        handleA.Activate();

        yield return WaitForCondition(
            () => !IsPickedUp(serverHandle),
            "The handle was never dropped.");
        Assert.That(HasSpawnedObject(server, handleId), Is.True,
            "The handle was destroyed before any door took it.");
        IgnoreReplicaCollisions(handleId);
        yield return new WaitForSecondsRealtime(0.5f);

        Assert.That(HasSpawnedObject(server, handleId), Is.True,
            "The handle was destroyed before any door took it.");

        // Carried by client B to a door twenty metres away: refused.
        NetworkTestPlayer playerB = GetNetworkTestPlayer(clientB);
        SuperSimpleDoorHandle handleB =
            GetSpawnedComponent<SuperSimpleDoorHandle>(clientB, handleId);
        AssertServerCanReach(clientB.Manager.LocalClientId, serverHandle);
        BeginPickupRequest(handleB, playerB);

        yield return WaitForCondition(
            () => IsPickedUp(serverHandle) &&
                  handleB.IsOwner &&
                  playerB.Orchestrator.States.IsCarrying,
            "Client B could not pick the handle up.");

        GetSpawnedComponent<EntranceDoor>(clientB, farDoorId).TryInsertHandle(itemId);
        yield return new WaitForSecondsRealtime(0.5f);

        Assert.That(GetSpawnedComponent<EntranceDoor>(server, farDoorId).IsUnlocked, Is.False,
            "The exit took a handle from somebody twenty metres away.");

        // At the door: taken, and only now destroyed when its carrier asks.
        GetSpawnedComponent<EntranceDoor>(clientB, nearDoorId).TryInsertHandle(itemId);

        yield return WaitForCondition(
            () => GetSpawnedComponent<EntranceDoor>(server, nearDoorId).IsUnlocked &&
                  serverHandle.IsConsumedServer,
            "The exit did not take the handle from somebody standing at it.");

        handleB.Activate();

        yield return WaitForCondition(
            () => !HasSpawnedObject(server, handleId),
            "A handle the door had taken was not destroyed.");
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
        networkTestPlayerPrefab.AddComponent<InPlayProbe>();
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
        handlePrefab = CreateNetworkItemPrefab<SuperSimpleDoorHandle>(
            "Network door handle test prefab",
            HandlePrefabHash,
            pickupData,
            viewModel);

        entranceDoorPrefab = Track(new GameObject("Network entrance door test prefab"));
        entranceDoorPrefab.SetActive(false);
        entranceDoorPrefab.transform.position = new Vector3(10000f, 10000f, 10000f);
        ConfigureNetworkObject(
            entranceDoorPrefab.AddComponent<NetworkObject>(),
            EntranceDoorPrefabHash);
        entranceDoorPrefab.AddComponent<BoxCollider>();
        EntranceDoor entranceDoor = entranceDoorPrefab.AddComponent<EntranceDoor>();
        PlayModeTestReflection.SetField(entranceDoor, "requiredHandleItemId", pickupData.ItemID);
        entranceDoorPrefab.SetActive(true);
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
        {
            PlayModeTestReflection.SetField(item, "model", model);

            // Made of more than one mesh, like the crumpled paper.
            for (int i = 0; i < 2; i++)
            {
                GameObject part = new($"Part {i}");
                part.transform.SetParent(prefab.transform, false);
                part.AddComponent<MeshFilter>();
                part.AddComponent<MeshRenderer>();
            }
        }

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
            targets[i].Manager.NetworkConfig.Prefabs.Add(
                new NetworkPrefab { Prefab = handlePrefab });
            targets[i].Manager.NetworkConfig.Prefabs.Add(
                new NetworkPrefab { Prefab = entranceDoorPrefab });
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
        PickupItem item,
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
