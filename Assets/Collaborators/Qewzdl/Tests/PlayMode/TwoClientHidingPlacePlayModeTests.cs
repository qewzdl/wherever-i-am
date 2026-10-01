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

internal sealed class HidingEntryEligibilityProbe :
    MonoBehaviour,
    IHidingEntryEligibility
{
    internal bool Allowed { get; set; } = true;

    public bool CanEnterHiding => Allowed;
}

[Category("Multiplayer")]
public sealed class TwoClientHidingPlacePlayModeTests : MultiEndpointPlayModeTest
{
    private const uint PlayerPrefabHash = 0x17A60011u;
    private const uint HidingPlacePrefabHash = 0x17A60012u;


    private Endpoint server;
    private Endpoint clientA;
    private Endpoint clientB;
    private GameObject playerPrefab;
    private GameObject hidingPlacePrefab;
    private AudioClip enterSound;
    private AudioClip exitSound;


    [UnityTearDown]
    public IEnumerator TearDown()
    {
        yield return StopEndpoints();
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
        HidingPlaceNavigationObstacle serverNavigation =
            GetComponent<HidingPlaceNavigationObstacle>(
                server,
                hidingPlaceId
            );
        HidingPlaceNavigationObstacle clientNavigationA =
            GetComponent<HidingPlaceNavigationObstacle>(
                clientA,
                hidingPlaceId
            );
        HidingPlaceNavigationObstacle clientNavigationB =
            GetComponent<HidingPlaceNavigationObstacle>(
                clientB,
                hidingPlaceId
            );

        Assert.That(serverNavigation.IsBlockingNavigation, Is.True);
        Assert.That(clientNavigationA.IsBlockingNavigation, Is.False);
        Assert.That(clientNavigationB.IsBlockingNavigation, Is.False);
        Assert.That(
            serverNavigation.GetComponent<NavMeshObstacle>().carveOnlyStationary,
            Is.True
        );
        List<HidingTransitionState> observedServerStates = new();
        List<HidingTransitionState> observedClientBStates = new();
        List<HidingNoiseCue> observedNoiseCues = new();
        serverPlace.StateChanged += (_, state) =>
            observedServerStates.Add(state);
        placeB.StateChanged += (_, state) =>
            observedClientBStates.Add(state);
        serverPlace.ServerNoiseRequested += (cue, _) =>
            observedNoiseCues.Add(cue);

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
        Assert.That(
            winner.HidingPose,
            Is.EqualTo(serverPlace.Configuration.HidingPose)
        );
        Assert.That(
            winner.CanPeek,
            Is.EqualTo(serverPlace.Configuration.AllowPeeking)
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
                  placeA.State == HidingTransitionState.Available &&
                  placeB.State == HidingTransitionState.Available &&
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
        CollectionAssert.AreEqual(
            new[]
            {
                HidingTransitionState.Entering,
                HidingTransitionState.Occupied,
                HidingTransitionState.Exiting,
                HidingTransitionState.Available
            },
            observedServerStates,
            "The server did not commit the complete hiding transition sequence."
        );
        CollectionAssert.AreEqual(
            observedServerStates,
            observedClientBStates,
            "A remote client did not observe the authoritative transition " +
            "sequence in the same order."
        );
        CollectionAssert.AreEqual(
            new[]
            {
                HidingNoiseCue.Enter,
                HidingNoiseCue.Exit
            },
            observedNoiseCues,
            "Gameplay noise cues were not emitted exactly once per transition."
        );
    }

    [UnityTest]
    public IEnumerator OccupantDisconnect_ReleasesPlace_ForRemainingClient()
    {
        yield return StartNetwork();

        // Side by side, not inside each other. Standing on the same spot,
        // player B's body was in the way of player A's look at the hiding
        // place - the server checks that nothing stands between - and
        // whether it blocked came down to how the copies of B had settled:
        // this failed now and then in a full run and never on its own.
        ulong playerAId = SpawnPlayer(clientA.Manager.LocalClientId);
        ulong playerBId = SpawnPlayer(
            clientB.Manager.LocalClientId,
            new Vector3(1.5f, 0f, 0f));
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

        PlayerHidingController playerB = GetComponent<PlayerHidingController>(
            clientB,
            playerBId
        );
        HidingPlaceInteractable placeB =
            GetComponent<HidingPlaceInteractable>(
                clientB,
                hidingPlaceId
            );

        clientA.Manager.Shutdown(discardMessageQueue: false);

        // Gone from client B as well, not only from the server. Every endpoint
        // here shares one physics scene, so client B's copy of player A, still
        // standing until the despawn reaches it, was in the way of the
        // server's look from B to the hiding place: the entry was refused now
        // and then in a full run, when that message came a frame late.
        yield return WaitForCondition(
            () => !clientA.Manager.IsListening &&
                  !server.Manager.SpawnManager.SpawnedObjects.ContainsKey(
                      playerAId
                  ) &&
                  !clientB.Manager.SpawnManager.SpawnedObjects.ContainsKey(
                      playerAId
                  ) &&
                  !serverPlace.IsOccupied &&
                  placeB.IsAvailable,
            "Disconnect did not despawn the occupant and release the place."
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

        Rigidbody recoveredBody = GetComponent<PlayerHidingController>(
            server,
            playerBId
        ).GetComponent<Rigidbody>();

        // Settled, and out. A hard teleport onto the floor takes a few physics
        // steps to settle, so waiting for the body to stop moving was the
        // right half of the idea - but on its own it is satisfied before the
        // recovery has begun, because a player still standing on the hiding
        // point is not moving either. This test then measured the inside of
        // the cupboard it was checking they had left, and did it about once
        // in three runs.
        yield return WaitForCondition(
            () => recoveredBody.linearVelocity.sqrMagnitude < 0.0001f &&
                  IsAtKnownSafeExit(recoveredBody.transform.position),
            "Recovered player did not settle at a safe exit after runtime " +
            "destruction."
        );

        Vector3 recoveredPosition =
            GetComponent<PlayerHidingController>(
                server,
                playerBId
            ).transform.position;

        Assert.That(
            IsAtKnownSafeExit(recoveredPosition),
            Is.True,
            $"Runtime destruction recovered the player at unexpected " +
            $"position {recoveredPosition}."
        );
    }

    [UnityTest]
    public IEnumerator BlockedLineOfSight_ServerRejectsEntry()
    {
        yield return StartNetwork();

        ulong playerId = SpawnPlayer(
            clientA.Manager.LocalClientId,
            Vector3.back * 2f
        );
        ulong hidingPlaceId = SpawnHidingPlace();

        yield return WaitForSpawnOnEveryEndpoint(
            playerId,
            hidingPlaceId
        );

        GameObject wall = Track(new GameObject("Hiding entry wall"));
        wall.transform.position = Vector3.back;
        BoxCollider wallCollider = wall.AddComponent<BoxCollider>();
        wallCollider.size = new Vector3(3f, 3f, 0.25f);
        Physics.SyncTransforms();

        PlayerHidingController player =
            GetComponent<PlayerHidingController>(
                clientA,
                playerId
            );
        HidingPlaceInteractable clientPlace =
            GetComponent<HidingPlaceInteractable>(
                clientA,
                hidingPlaceId
            );
        HidingPlaceInteractable serverPlace =
            GetComponent<HidingPlaceInteractable>(
                server,
                hidingPlaceId
            );

        Assert.That(clientPlace.TryRequestEnter(player), Is.True);

        yield return new WaitForSecondsRealtime(0.25f);

        Assert.That(serverPlace.IsOccupied, Is.False);
        Assert.That(
            GetComponent<PlayerHidingController>(
                server,
                playerId
            ).IsHidden,
            Is.False
        );

        Object.Destroy(wall);
        yield return null;
        Physics.SyncTransforms();

        // This assertion has a history of failing intermittently in full runs
        // and never in isolation, and "the server stayed Available" on its own
        // says nothing about which of TryEnterServer's three checks refused.
        // Capture the inputs to those checks up front so the next occurrence
        // explains itself instead of costing another bisect.
        PlayerHidingController serverPlayer =
            GetComponent<PlayerHidingController>(server, playerId);
        Vector3 serverPlayerPosition = serverPlayer.transform.position;
        Vector3 clientPlayerPosition = player.transform.position;

        Assert.That(clientPlace.TryRequestEnter(player), Is.True);

        float enteringTimeout = Time.realtimeSinceStartup + TimeoutSeconds;

        while (serverPlace.State != HidingTransitionState.Entering &&
               Time.realtimeSinceStartup < enteringTimeout)
        {
            yield return null;
        }

        Assert.That(
            serverPlace.State == HidingTransitionState.Entering,
            Is.True,
            "Server did not expose the entering transition. " +
            $"serverPlayerPos={serverPlayerPosition} " +
            $"clientPlayerPos={clientPlayerPosition} " +
            $"placePos={serverPlace.transform.position} " +
            $"anchorPos={serverPlace.EnemyInvestigationPosition} " +
            $"maxInteractionDistance={serverPlace.Configuration.MaxInteractionDistance} " +
            $"serverState={serverPlace.State} " +
            $"serverAvailable={serverPlace.IsAvailable} " +
            $"serverPlayerNow={serverPlayer.transform.position} " +
            $"clientPlayerNow={player.transform.position}"
        );

        Assert.That(serverPlace.IsAvailable, Is.False);
        Assert.That(
            serverPlace.TryInvestigateServer(
                serverPlace.EnemyInvestigationPosition
            ),
            Is.False,
            "Enemy check must wait until the entry transition commits."
        );

        yield return WaitForCondition(
            () => serverPlace.OccupantNetworkObjectId == playerId &&
                  player.IsHidden,
            "Entry remained blocked after line of sight was restored."
        );
    }

    // Range is measured to the nearest surface of the place, not to its
    // middle, so the box's own half depth is not part of the gap. Standing at
    // 3 leaves a tenth of a metre of it, which this fixture's replica drift
    // can eat on its own.
    [UnityTest]
    public IEnumerator OutOfRangePlayer_ServerRejectsEntry()
    {
        yield return StartNetwork();

        ulong playerId = SpawnPlayer(
            clientA.Manager.LocalClientId,
            Vector3.back * 4f
        );
        ulong hidingPlaceId = SpawnHidingPlace();

        yield return WaitForSpawnOnEveryEndpoint(
            playerId,
            hidingPlaceId
        );

        PlayerHidingController player =
            GetComponent<PlayerHidingController>(
                clientA,
                playerId
            );
        HidingPlaceInteractable clientPlace =
            GetComponent<HidingPlaceInteractable>(
                clientA,
                hidingPlaceId
            );
        HidingPlaceInteractable serverPlace =
            GetComponent<HidingPlaceInteractable>(
                server,
                hidingPlaceId
            );

        Assert.That(clientPlace.TryRequestEnter(player), Is.True);

        yield return new WaitForSecondsRealtime(0.25f);

        Assert.That(serverPlace.IsOccupied, Is.False);
        Assert.That(player.IsHidden, Is.False);
    }

    // What a client's player actually looks like on the server: PlayerSetup
    // destroys the movement component on every copy that is not locally
    // controlled, and on the server that is every client. The entry gate asked
    // for one anyway, so the only player who could ever get in was the host,
    // whose own copy keeps it. The fixture hands every player one, which is
    // why nothing here noticed.
    [UnityTest]
    public IEnumerator PlayerWithoutMovementComponent_ServerAcceptsEntry()
    {
        yield return StartNetwork();

        ulong playerId = SpawnPlayer(clientA.Manager.LocalClientId);
        ulong hidingPlaceId = SpawnHidingPlace();

        yield return WaitForSpawnOnEveryEndpoint(
            playerId,
            hidingPlaceId
        );

        PlayerHidingController serverPlayer =
            GetComponent<PlayerHidingController>(
                server,
                playerId
            );

        Object.DestroyImmediate(serverPlayer.GetComponent<PlayerController>());

        Assert.That(
            serverPlayer.GetComponent<PlayerController>(),
            Is.Null,
            "The server's copy still has a movement component, so this is " +
            "not shaped like a real client's player."
        );

        PlayerHidingController player =
            GetComponent<PlayerHidingController>(
                clientA,
                playerId
            );
        HidingPlaceInteractable clientPlace =
            GetComponent<HidingPlaceInteractable>(
                clientA,
                hidingPlaceId
            );
        HidingPlaceInteractable serverPlace =
            GetComponent<HidingPlaceInteractable>(
                server,
                hidingPlaceId
            );

        Assert.That(clientPlace.TryRequestEnter(player), Is.True);

        yield return WaitForCondition(
            () => serverPlace.IsOccupied,
            "A client was refused entry because the server's copy of its " +
            "player has no movement component."
        );
    }

    // The reach that offers a hiding place is a ray that stops at its surface,
    // so a player that reach can serve has to be able to get in. Measured to
    // the middle of the place instead, this spot is out of range and the entry
    // is refused - which is what the check used to do, and why entering meant
    // walking closer than picking something up did.
    [UnityTest]
    public IEnumerator PlayerWithinReachOfSurface_ServerAcceptsEntry()
    {
        yield return StartNetwork();

        ulong playerId = SpawnPlayer(
            clientA.Manager.LocalClientId,
            Vector3.back * 3.25f
        );
        ulong hidingPlaceId = SpawnHidingPlace();

        yield return WaitForSpawnOnEveryEndpoint(
            playerId,
            hidingPlaceId
        );

        // Three metres deep, so its near face is 1.75 away and its middle
        // 3.25 - three quarters of a metre either side of the 2.5 it allows,
        // so neither answer rests on a rounding.
        WidenHidingPlace(hidingPlaceId, 3f);

        PlayerHidingController player =
            GetComponent<PlayerHidingController>(
                clientA,
                playerId
            );
        HidingPlaceInteractable clientPlace =
            GetComponent<HidingPlaceInteractable>(
                clientA,
                hidingPlaceId
            );
        HidingPlaceInteractable serverPlace =
            GetComponent<HidingPlaceInteractable>(
                server,
                hidingPlaceId
            );
        PlayerHidingController serverPlayer =
            GetComponent<PlayerHidingController>(
                server,
                playerId
            );

        Assert.That(
            Vector3.Distance(
                serverPlayer.transform.position,
                serverPlace.transform.position
            ),
            Is.GreaterThan(serverPlace.Configuration.MaxInteractionDistance),
            "The player stands within range of the middle too, so this " +
            "fixture cannot tell the two measurements apart."
        );

        Assert.That(clientPlace.TryRequestEnter(player), Is.True);

        yield return WaitForCondition(
            () => serverPlace.IsOccupied,
            "A player standing within reach of the hiding place's surface " +
            "was refused entry."
        );
    }

    private void WidenHidingPlace(ulong hidingPlaceId, float depth)
    {
        Endpoint[] endpoints = { server, clientA, clientB };

        for (int i = 0; i < endpoints.Length; i++)
        {
            BoxCollider box =
                GetComponent<HidingPlaceInteractable>(
                    endpoints[i],
                    hidingPlaceId
                ).GetComponent<BoxCollider>();

            box.size = new Vector3(box.size.x, box.size.y, depth);
        }

        Physics.SyncTransforms();
    }

    [UnityTest]
    public IEnumerator UnavailablePlayer_ServerRejectsEntry()
    {
        yield return StartNetwork();

        ulong playerId = SpawnPlayer(clientA.Manager.LocalClientId);
        ulong hidingPlaceId = SpawnHidingPlace();

        yield return WaitForSpawnOnEveryEndpoint(
            playerId,
            hidingPlaceId
        );

        HidingEntryEligibilityProbe serverEligibility =
            GetComponent<HidingEntryEligibilityProbe>(
                server,
                playerId
            );
        serverEligibility.Allowed = false;

        PlayerHidingController player =
            GetComponent<PlayerHidingController>(
                clientA,
                playerId
            );
        HidingPlaceInteractable clientPlace =
            GetComponent<HidingPlaceInteractable>(
                clientA,
                hidingPlaceId
            );
        HidingPlaceInteractable serverPlace =
            GetComponent<HidingPlaceInteractable>(
                server,
                hidingPlaceId
            );

        Assert.That(clientPlace.TryRequestEnter(player), Is.True);

        yield return new WaitForSecondsRealtime(0.25f);

        Assert.That(serverPlace.IsOccupied, Is.False);
        Assert.That(player.IsHidden, Is.False);

        serverEligibility.Allowed = true;
        PlayerHidingController serverPlayer =
            GetComponent<PlayerHidingController>(
                server,
                playerId
            );
        Assert.That(
            (bool)PlayModeTestReflection.Invoke(
                serverPlayer,
                "CanEnterHidingServer"
            ),
            Is.True,
            "The server still considered the player unavailable after " +
            "the eligibility condition was restored."
        );
    }

    [UnityTest]
    public IEnumerator EnemyInvestigation_OpensOccupiedPlace_AndRevealsPlayer()
    {
        yield return StartNetwork();

        ulong playerId = SpawnPlayer(clientA.Manager.LocalClientId);
        ulong hidingPlaceId = SpawnHidingPlace();

        yield return WaitForSpawnOnEveryEndpoint(
            playerId,
            hidingPlaceId
        );

        PlayerHidingController player =
            GetComponent<PlayerHidingController>(
                clientA,
                playerId
            );
        HidingPlaceInteractable clientPlace =
            GetComponent<HidingPlaceInteractable>(
                clientA,
                hidingPlaceId
            );
        HidingPlaceInteractable serverPlace =
            GetComponent<HidingPlaceInteractable>(
                server,
                hidingPlaceId
            );

        Assert.That(clientPlace.TryRequestEnter(player), Is.True);

        yield return WaitForCondition(
            () => serverPlace.State ==
                  HidingTransitionState.Occupied &&
                  player.IsHidden,
            "Player did not become fully hidden before investigation."
        );

        float investigationDistance =
            serverPlace.Configuration.EnemyInvestigationDistance;

        Assert.That(
            serverPlace.TryInvestigateServer(
                serverPlace.EnemyInvestigationPosition +
                Vector3.forward * (investigationDistance + 1f)
            ),
            Is.False,
            "An enemy outside the configured investigation distance " +
            "must not open the hiding place."
        );
        Assert.That(
            serverPlace.State,
            Is.EqualTo(HidingTransitionState.Occupied)
        );

        Assert.That(
            serverPlace.TryInvestigateServer(
                serverPlace.EnemyInvestigationPosition
            ),
            Is.True
        );
        Assert.That(
            serverPlace.State,
            Is.EqualTo(HidingTransitionState.Exiting)
        );

        yield return WaitForCondition(
            () => serverPlace.State ==
                  HidingTransitionState.Available &&
                  !serverPlace.IsOccupied &&
                  !player.IsInHidingSequence,
            "Enemy investigation did not reveal and release the player."
        );
    }

    [UnityTest]
    public IEnumerator DespawnDuringEntering_RecoversPlayerAndUnlocksMovement()
    {
        yield return StartNetwork();

        ulong playerId = SpawnPlayer(clientA.Manager.LocalClientId);
        ulong hidingPlaceId = SpawnHidingPlace();

        yield return WaitForSpawnOnEveryEndpoint(
            playerId,
            hidingPlaceId
        );

        PlayerHidingController clientPlayer =
            GetComponent<PlayerHidingController>(
                clientA,
                playerId
            );
        HidingPlaceInteractable clientPlace =
            GetComponent<HidingPlaceInteractable>(
                clientA,
                hidingPlaceId
            );
        HidingPlaceInteractable serverPlace =
            GetComponent<HidingPlaceInteractable>(
                server,
                hidingPlaceId
            );

        Assert.That(clientPlace.TryRequestEnter(clientPlayer), Is.True);

        yield return WaitForCondition(
            () => serverPlace.State ==
                  HidingTransitionState.Entering &&
                  GetComponent<PlayerHidingController>(
                      server,
                      playerId
                  ).IsInHidingSequence &&
                  clientPlayer.HidingState ==
                  HidingTransitionState.Entering,
            "The entering transition did not replicate to the owner."
        );

        Assert.That(clientPlayer.IsHidden, Is.False);
        serverPlace.NetworkObject.Despawn(destroy: true);

        yield return WaitForCondition(
            () => !server.Manager.SpawnManager.SpawnedObjects.ContainsKey(
                      hidingPlaceId
                  ) &&
                  !GetComponent<PlayerHidingController>(
                      server,
                      playerId
                  ).IsInHidingSequence &&
                  !clientPlayer.IsInHidingSequence,
            "Runtime destruction during entry did not roll back the " +
            "player hiding sequence."
        );

        AssertPlayerRestored(clientPlayer);
    }

    // Caught on the way in. The climb used to finish regardless, and the
    // place stayed Occupied by somebody no longer playing, shut to everybody
    // for the rest of the match - and letting them out put back the body
    // that being caught had taken out of the room.
    [UnityTest]
    public IEnumerator OccupantLeavingPlay_ReleasesPlace_AndBodyStaysOut()
    {
        yield return StartNetwork();

        ulong playerId = SpawnPlayer(clientA.Manager.LocalClientId);
        ulong hidingPlaceId = SpawnHidingPlace();

        yield return WaitForSpawnOnEveryEndpoint(
            playerId,
            hidingPlaceId
        );

        PlayerHidingController clientPlayer =
            GetComponent<PlayerHidingController>(clientA, playerId);
        PlayerHidingController serverPlayer =
            GetComponent<PlayerHidingController>(server, playerId);
        HidingPlaceInteractable clientPlace =
            GetComponent<HidingPlaceInteractable>(clientA, hidingPlaceId);
        HidingPlaceInteractable serverPlace =
            GetComponent<HidingPlaceInteractable>(server, hidingPlaceId);

        Assert.That(clientPlace.TryRequestEnter(clientPlayer), Is.True);

        yield return WaitForCondition(
            () => serverPlace.State == HidingTransitionState.Entering &&
                  clientPlayer.HidingState == HidingTransitionState.Entering,
            "The entering transition did not replicate to the owner."
        );

        // What being caught does on every machine: out of play, and the
        // body out of the world.
        foreach (Endpoint endpoint in endpoints)
        {
            PlayerHidingController copy =
                GetComponent<PlayerHidingController>(endpoint, playerId);
            copy.GetComponent<InPlayProbe>().IsInPlay = false;
            copy.GetComponent<MeshRenderer>().enabled = false;
            copy.GetComponent<CapsuleCollider>().enabled = false;
        }

        yield return WaitForCondition(
            () => serverPlace.State == HidingTransitionState.Available &&
                  !serverPlace.IsOccupied &&
                  clientPlace.IsAvailable &&
                  !serverPlayer.IsInHidingSequence &&
                  !clientPlayer.IsInHidingSequence,
            "A player caught while climbing in kept the hiding place."
        );

        foreach (Endpoint endpoint in endpoints)
        {
            PlayerHidingController copy =
                GetComponent<PlayerHidingController>(endpoint, playerId);

            Assert.That(
                copy.GetComponent<MeshRenderer>().enabled ||
                copy.GetComponent<CapsuleCollider>().enabled,
                Is.False,
                "Letting the caught player out of the hiding place put " +
                "their body back."
            );
        }
    }

    // Put into a hiding place and back out, a player is moved a metre or two
    // in one frame. That read as a sprint, and the first stride of a sprint
    // lands at once: a running footstep on every machine, and on the server a
    // running noise the enemy heard - from the cupboard the player had just
    // hidden in.
    [UnityTest]
    public IEnumerator ClimbingInAndOut_MakesNoFootstep()
    {
        yield return StartNetwork();

        // Clear of the hiding place: spawned inside it, the player is pushed
        // out at a walk, and those steps are real.
        ulong playerId = SpawnPlayer(
            clientA.Manager.LocalClientId,
            new Vector3(0f, 0f, -1f));
        ulong hidingPlaceId = SpawnHidingPlace();

        yield return WaitForSpawnOnEveryEndpoint(playerId, hidingPlaceId);

        int footfalls = 0;
        System.Text.StringBuilder heard = new();

        foreach (Endpoint endpoint in endpoints)
        {
            FootstepEmitter emitter = GetComponent<FootstepEmitter>(endpoint, playerId);
            PlayerHidingController copy = emitter.GetComponent<PlayerHidingController>();
            emitter.Footfall += () =>
            {
                footfalls++;
                heard.AppendLine(
                    $"{Time.time:F3} {endpoint.Manager.name} at {emitter.transform.position} " +
                    $"state={copy.HidingState}");
            };
        }

        PlayerHidingController clientPlayer =
            GetComponent<PlayerHidingController>(clientA, playerId);
        HidingPlaceInteractable clientPlace =
            GetComponent<HidingPlaceInteractable>(clientA, hidingPlaceId);

        yield return new WaitForSecondsRealtime(0.5f);
        footfalls = 0;
        heard.Clear();
        Assert.That(clientPlace.TryRequestEnter(clientPlayer), Is.True);

        yield return WaitForCondition(
            () => endpoints.TrueForAll(e => GetComponent<PlayerHidingController>(e, playerId).IsHidden),
            "The player did not end up hidden on every machine.");
        yield return new WaitForSecondsRealtime(0.5f);

        clientPlayer.RequestExitHiding();

        yield return WaitForCondition(
            () => endpoints.TrueForAll(e => !GetComponent<PlayerHidingController>(e, playerId).IsInHidingSequence),
            "The player did not get out on every machine.");
        yield return new WaitForSecondsRealtime(0.5f);

        Assert.That(footfalls, Is.Zero,
            "Being put into and out of a hiding place was heard as footsteps:\n" + heard);

        // And walking still is, or the silence above proves nothing.
        Rigidbody ownBody = clientPlayer.GetComponent<Rigidbody>();

        for (float walked = 0f; walked < 0.8f; walked += Time.deltaTime)
        {
            ownBody.linearVelocity = new Vector3(2.5f, 0f, 0f);
            yield return null;
        }

        ownBody.linearVelocity = Vector3.zero;
        Assert.That(footfalls, Is.GreaterThan(0), "Walking made no footstep either.");
    }

    [UnityTest]
    public IEnumerator DespawnDuringExiting_RecoversPlayerAndUnlocksMovement()
    {
        yield return StartNetwork();

        ulong playerId = SpawnPlayer(clientA.Manager.LocalClientId);
        ulong hidingPlaceId = SpawnHidingPlace();

        yield return WaitForSpawnOnEveryEndpoint(
            playerId,
            hidingPlaceId
        );

        PlayerHidingController clientPlayer =
            GetComponent<PlayerHidingController>(
                clientA,
                playerId
            );
        HidingPlaceInteractable clientPlace =
            GetComponent<HidingPlaceInteractable>(
                clientA,
                hidingPlaceId
            );
        HidingPlaceInteractable serverPlace =
            GetComponent<HidingPlaceInteractable>(
                server,
                hidingPlaceId
            );

        Assert.That(clientPlace.TryRequestEnter(clientPlayer), Is.True);

        yield return WaitForCondition(
            () => serverPlace.State ==
                  HidingTransitionState.Occupied &&
                  clientPlayer.IsHidden,
            "Player did not enter before the exit transition test."
        );

        clientPlayer.RequestExitHiding();

        yield return WaitForCondition(
            () => serverPlace.State ==
                  HidingTransitionState.Exiting &&
                  clientPlayer.HidingState ==
                  HidingTransitionState.Exiting,
            "The exiting transition did not replicate to the owner."
        );

        Assert.That(clientPlayer.IsHidden, Is.False);
        serverPlace.NetworkObject.Despawn(destroy: true);

        yield return WaitForCondition(
            () => !server.Manager.SpawnManager.SpawnedObjects.ContainsKey(
                      hidingPlaceId
                  ) &&
                  !GetComponent<PlayerHidingController>(
                      server,
                      playerId
                  ).IsInHidingSequence &&
                  !clientPlayer.IsInHidingSequence,
            "Runtime destruction during exit did not complete safe recovery."
        );

        AssertPlayerRestored(clientPlayer);
    }

    [UnityTest]
    public IEnumerator BlockedExit_KeepsPlayerHidden_ThenUsesFallback()
    {
        yield return StartNetwork();

        ulong playerId = SpawnPlayer(clientA.Manager.LocalClientId);
        ulong hidingPlaceId = SpawnHidingPlace();

        yield return WaitForSpawnOnEveryEndpoint(
            playerId,
            hidingPlaceId
        );

        PlayerHidingController player =
            GetComponent<PlayerHidingController>(
                clientA,
                playerId
            );
        HidingPlaceInteractable clientPlace =
            GetComponent<HidingPlaceInteractable>(
                clientA,
                hidingPlaceId
            );
        HidingPlaceInteractable serverPlace =
            GetComponent<HidingPlaceInteractable>(
                server,
                hidingPlaceId
            );

        Assert.That(clientPlace.TryRequestEnter(player), Is.True);

        yield return WaitForCondition(
            () => serverPlace.OccupantNetworkObjectId == playerId &&
                  player.IsHidden,
            "Player did not enter before exit validation."
        );

        GameObject primaryBlocker =
            CreateExitBlocker("Primary exit blocker", Vector3.back);
        GameObject fallbackBlocker =
            CreateExitBlocker("Fallback exit blocker", Vector3.right * 2f);

        player.RequestExitHiding();
        yield return new WaitForSecondsRealtime(0.25f);

        Assert.That(serverPlace.IsOccupied, Is.True);
        Assert.That(player.IsHidden, Is.True);

        Object.Destroy(fallbackBlocker);
        yield return null;
        Physics.SyncTransforms();

        player.RequestExitHiding();

        yield return WaitForCondition(
            () => !serverPlace.IsOccupied &&
                  !player.IsHidden &&
                  Vector3.Distance(
                      player.transform.position,
                      GroundedExit(Vector3.right * 2f)
                  ) < 0.05f,
            "Player did not use the first available fallback exit."
        );

        Assert.That(
            Vector3.Distance(
                player.transform.position,
                GroundedExit(Vector3.right * 2f)
            ),
            Is.LessThan(0.05f)
        );

        Object.Destroy(primaryBlocker);
    }

    // Somebody climbing in and out is heard by everybody, however quickly
    // they do it. With no time given to the climb, the place went from free
    // to taken and back inside a single call on the server; only the end of
    // it reached anybody else, and only the host heard the sounds.
    [UnityTest]
    public IEnumerator HidingSounds_ReachEveryoneWhenClimbingTakesNoTime()
    {
        yield return StartNetwork();

        HidingPlaceData data = hidingPlacePrefab
            .GetComponent<HidingPlaceInteractable>()
            .Configuration;
        PlayModeTestReflection.SetField(data, "enterDuration", 0f);
        PlayModeTestReflection.SetField(data, "exitDuration", 0f);

        ulong playerId = SpawnPlayer(clientA.Manager.LocalClientId);
        ulong hidingPlaceId = SpawnHidingPlace();

        yield return WaitForSpawnOnEveryEndpoint(
            playerId,
            hidingPlaceId
        );

        PlayerHidingController player =
            GetComponent<PlayerHidingController>(clientA, playerId);
        HidingPlacePresentation hiderSees =
            GetComponent<HidingPlacePresentation>(clientA, hidingPlaceId);
        HidingPlacePresentation watcherSees =
            GetComponent<HidingPlacePresentation>(clientB, hidingPlaceId);

        Assert.That(
            GetComponent<HidingPlaceInteractable>(clientA, hidingPlaceId)
                .TryRequestEnter(player),
            Is.True);

        yield return WaitForCondition(
            () => hiderSees.LastPlayedClip == enterSound &&
                  watcherSees.LastPlayedClip == enterSound,
            "Somebody climbed in and not everybody heard it."
        );

        player.RequestExitHiding();

        yield return WaitForCondition(
            () => hiderSees.LastPlayedClip == exitSound &&
                  watcherSees.LastPlayedClip == exitSound,
            "Somebody climbed out and not everybody heard it."
        );
    }

    // A free exit on the far side of a wall is still on the far side of a
    // wall. The spot itself passes every overlap test, which is how players
    // used to come out of a hiding place into the next room.
    [UnityTest]
    public IEnumerator ExitBehindAWall_IsNeverTaken()
    {
        yield return StartNetwork();

        ulong playerId = SpawnPlayer(clientA.Manager.LocalClientId);
        ulong hidingPlaceId = SpawnHidingPlace();

        yield return WaitForSpawnOnEveryEndpoint(
            playerId,
            hidingPlaceId
        );

        PlayerHidingController player =
            GetComponent<PlayerHidingController>(
                clientA,
                playerId
            );
        HidingPlaceInteractable clientPlace =
            GetComponent<HidingPlaceInteractable>(
                clientA,
                hidingPlaceId
            );
        HidingPlaceInteractable serverPlace =
            GetComponent<HidingPlaceInteractable>(
                server,
                hidingPlaceId
            );

        Assert.That(clientPlace.TryRequestEnter(player), Is.True);

        yield return WaitForCondition(
            () => serverPlace.OccupantNetworkObjectId == playerId &&
                  player.IsHidden,
            "Player did not enter before exit validation."
        );

        // The primary exit is blocked outright; the fallback is free but
        // stands behind a post between it and the hiding point.
        GameObject primaryBlocker =
            CreateExitBlocker("Primary exit blocker", Vector3.back);
        GameObject wall = Track(new GameObject("Wall before the fallback"));
        wall.transform.position = new Vector3(1f, 1f, 0.5f);
        wall.AddComponent<BoxCollider>().size = new Vector3(0.2f, 4f, 0.2f);
        Physics.SyncTransforms();

        player.RequestExitHiding();
        yield return new WaitForSecondsRealtime(0.5f);

        Assert.That(
            Vector3.Distance(
                player.transform.position,
                GroundedExit(Vector3.right * 2f)
            ),
            Is.GreaterThan(0.5f),
            "The player came out on the far side of a wall."
        );

        Object.Destroy(primaryBlocker);
        Object.Destroy(wall);
    }

    [UnityTest]
    public IEnumerator GroundLevelExitAnchor_PlacesCapsuleAboveFloor()
    {
        yield return StartNetwork();

        GameObject floor = Track(new GameObject("Exit validation floor"));
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        BoxCollider floorCollider = floor.AddComponent<BoxCollider>();
        floorCollider.size = new Vector3(8f, 1f, 8f);
        PlayModeTestReflection.SetField(
            hidingPlacePrefab
                .GetComponent<HidingPlaceInteractable>()
                .Configuration,
            "requireEntryLineOfSight",
            false
        );
        Physics.SyncTransforms();

        ulong playerId = SpawnPlayer(
            clientA.Manager.LocalClientId,
            new Vector3(0f, 1f, -2f)
        );
        ulong hidingPlaceId = SpawnHidingPlace();

        yield return WaitForSpawnOnEveryEndpoint(playerId, hidingPlaceId);

        PlayerHidingController player =
            GetComponent<PlayerHidingController>(clientA, playerId);
        HidingPlaceInteractable clientPlace =
            GetComponent<HidingPlaceInteractable>(clientA, hidingPlaceId);
        HidingPlaceInteractable serverPlace =
            GetComponent<HidingPlaceInteractable>(server, hidingPlaceId);

        Assert.That(clientPlace.TryRequestEnter(player), Is.True);

        yield return WaitForCondition(
            () => serverPlace.IsOccupied && player.IsHidden,
            "Player did not enter before the grounded exit test."
        );

        player.RequestExitHiding();

        yield return WaitForCondition(
            () => !serverPlace.IsOccupied &&
                  !player.IsHidden &&
                  Mathf.Abs(player.transform.position.y - 1f) < 0.05f,
            "A floor-level exit anchor incorrectly blocked hiding exit."
        );

        Physics.SyncTransforms();

        CapsuleCollider playerCollider =
            player.GetComponent<CapsuleCollider>();
        Assert.That(
            player.transform.position.y,
            Is.EqualTo(1f).Within(0.05f)
        );
        Assert.That(
            playerCollider.bounds.min.y,
            Is.GreaterThanOrEqualTo(-0.01f)
        );
    }

    private static void AssertPlayerRestored(
        PlayerHidingController player
    )
    {
        Assert.That(player.IsInHidingSequence, Is.False);
        Assert.That(
            player.GetComponent<Rigidbody>().constraints,
            Is.EqualTo(RigidbodyConstraints.None)
        );
        Assert.That(player.GetComponent<Collider>().enabled, Is.True);
        Assert.That(player.GetComponent<Renderer>().enabled, Is.True);
        Assert.That(
            player.GetComponent<PlayerController>().IsMovementActive,
            Is.True
        );
    }

    private static bool IsAtKnownSafeExit(Vector3 position)
    {
        return Vector3.Distance(
                   position,
                   GroundedExit(Vector3.back)
               ) < 0.05f ||
               Vector3.Distance(
                   position,
                   GroundedExit(Vector3.right * 2f)
               ) < 0.05f;
    }

    private static Vector3 GroundedExit(Vector3 groundPosition)
    {
        return groundPosition + Vector3.up;
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
        playerPrefab.layer = LayerMask.NameToLayer("Player");
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
        CapsuleCollider bodyCollider =
            playerPrefab.AddComponent<CapsuleCollider>();
        bodyCollider.height = 2f;
        playerPrefab.AddComponent<MeshRenderer>();
        playerPrefab.AddComponent<HidingEntryEligibilityProbe>();
        playerPrefab.AddComponent<InPlayProbe>();

        // Walks at 2.5, heard as walking from 1.875 and as running from 3.125.
        PlayerMovementProfile gaits =
            Track(ScriptableObject.CreateInstance<PlayerMovementProfile>());
        PlayModeTestReflection.SetField(gaits, "walkSpeed", 2.5f);
        PlayModeTestReflection.SetField(gaits, "runSpeedMultiplier", 1.5f);
        PlayModeTestReflection.SetField(gaits, "crouchSpeedMultiplier", 0.5f);
        PlayModeTestReflection.SetField(
            playerPrefab.AddComponent<FootstepEmitter>(),
            "movement",
            gaits);

        PlayerController movement =
            playerPrefab.AddComponent<PlayerController>();
        movement.enabled = false;
        PlayerActionGate actionGate =
            playerPrefab.AddComponent<PlayerActionGate>();

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
            "bodyCollider",
            bodyCollider
        );
        PlayModeTestReflection.SetField(
            hiding,
            "playerController",
            movement
        );
        PlayModeTestReflection.SetField(
            hiding,
            "playerActionGateSource",
            actionGate
        );
        PlayModeTestReflection.SetField(
            hiding,
            "visualRoot",
            playerPrefab.transform
        );
        PlayModeTestReflection.SetField(
            hiding,
            "gameplayColliders",
            new Collider[] { bodyCollider }
        );
        PlayModeTestReflection.SetField(
            hiding,
            "hitboxColliders",
            System.Array.Empty<Collider>()
        );

        playerPrefab.SetActive(true);

        HidingPlaceData hidingData =
            Track(ScriptableObject.CreateInstance<HidingPlaceData>());
        PlayModeTestReflection.SetField(hidingData, "enterDuration", 0.2f);
        PlayModeTestReflection.SetField(hidingData, "exitDuration", 0.2f);
        enterSound = Track(AudioClip.Create("Climb in", 441, 1, 44100, false));
        exitSound = Track(AudioClip.Create("Climb out", 441, 1, 44100, false));
        PlayModeTestReflection.SetField(hidingData, "enterSound", enterSound);
        PlayModeTestReflection.SetField(hidingData, "exitSound", exitSound);

        // Three NetworkManagers share one physics scene, so every player
        // exists three times. The test base stops the copies shoving each
        // other, but it leaves them in overlap queries.
        //
        // The exit resolver defaults to a mask of everything and excludes
        // only colliders belonging to the player it is placing. A replica is
        // a different NetworkObject, so it reads as somebody standing in the
        // exit; the release then falls through to the emergency path, which
        // accepts the player's current pose and leaves them in the hiding
        // place they were just let out of.
        //
        // Dropping the player layer from the mask is what makes the query see
        // what production sees: one body per player. The cost is that this
        // fixture cannot cover an exit blocked by another player - the
        // deliberate blockers it does use are plain Default-layer colliders.
        int playerObstructionLayer = LayerMask.NameToLayer("Player");

        if (playerObstructionLayer >= 0)
        {
            PlayModeTestReflection.SetField(
                hidingData,
                "exitObstructionMask",
                (LayerMask)(~(1 << playerObstructionLayer))
            );
        }

        hidingPlacePrefab = Track(
            new GameObject("Hiding place prefab")
        );
        hidingPlacePrefab.SetActive(false);
        hidingPlacePrefab.layer = LayerMask.NameToLayer("Interactable");
        hidingPlacePrefab.transform.position =
            new Vector3(10000f, 10000f, 10000f);

        NetworkObject placeNetworkObject =
            hidingPlacePrefab.AddComponent<NetworkObject>();
        ConfigureNetworkPrefab(
            placeNetworkObject,
            HidingPlacePrefabHash
        );
        BoxCollider hidingPlaceCollider =
            hidingPlacePrefab.AddComponent<BoxCollider>();
        hidingPlaceCollider.size = new Vector3(0.8f, 0.8f, 0.8f);

        Transform hidingPoint = CreateChild(
            hidingPlacePrefab.transform,
            "Hiding Point",
            Vector3.forward
        );
        Transform cameraAnchor = CreateChild(
            hidingPlacePrefab.transform,
            "Camera Anchor",
            Vector3.up
        );
        Transform exitPoint = CreateChild(
            hidingPlacePrefab.transform,
            "Exit Point",
            Vector3.back
        );
        Transform fallbackExitPoint = CreateChild(
            hidingPlacePrefab.transform,
            "Fallback Exit Point",
            Vector3.right * 2f
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
            "cameraAnchor",
            cameraAnchor
        );
        PlayModeTestReflection.SetField(
            hidingPlace,
            "exitPoint",
            exitPoint
        );
        PlayModeTestReflection.SetField(
            hidingPlace,
            "fallbackExitPoints",
            new[] { fallbackExitPoint }
        );
        hidingPlacePrefab.AddComponent<AudioSource>();
        hidingPlacePrefab.AddComponent<HidingPlacePresentation>();

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

    private ulong SpawnPlayer(
        ulong ownerClientId,
        Vector3? spawnPosition = null
    )
    {
        GameObject instance = Object.Instantiate(
            playerPrefab,
            spawnPosition ?? Vector3.zero,
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

    private GameObject CreateExitBlocker(
        string blockerName,
        Vector3 position
    )
    {
        GameObject blocker = Track(new GameObject(blockerName));
        blocker.transform.position = position;
        BoxCollider collider = blocker.AddComponent<BoxCollider>();
        collider.size = new Vector3(1.5f, 3f, 1.5f);
        Physics.SyncTransforms();
        return blocker;
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

        // Newly spawned/positioned colliders (e.g. a player's body
        // capsule) aren't guaranteed to be reflected in Physics queries
        // (Collider.bounds, raycasts) until the physics world syncs.
        // Callers immediately run line-of-sight/overlap checks against
        // these objects, so force the sync here once for everyone.
        Physics.SyncTransforms();
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
}
