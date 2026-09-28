using Unity.Netcode;
using UnityEngine;

internal sealed class HidingExitPlacementResolver
{
    private const int MaxOverlaps = 32;

    private static readonly Vector3[] EmergencyDirections =
    {
        Vector3.forward,
        Vector3.back,
        Vector3.left,
        Vector3.right,
        new Vector3(1f, 0f, 1f).normalized,
        new Vector3(1f, 0f, -1f).normalized,
        new Vector3(-1f, 0f, 1f).normalized,
        new Vector3(-1f, 0f, -1f).normalized
    };

    private static readonly float[] EmergencyDistances =
    {
        0.75f,
        1.5f,
        2.5f
    };

    private readonly Collider[] overlaps = new Collider[MaxOverlaps];
    private readonly RaycastHit[] hits = new RaycastHit[MaxOverlaps];

    internal bool TryResolve(
        PlayerHidingController player,
        Transform hidingPoint,
        Transform primaryExit,
        Transform[] fallbackExits,
        bool alignPlayerRotation,
        HidingPlaceData settings,
        bool includeRecoveryPose,
        out Pose exitPose
    )
    {
        exitPose = default;

        if (player == null || settings == null)
        {
            return false;
        }

        if (TryResolveTransform(
                player,
                hidingPoint,
                primaryExit,
                alignPlayerRotation,
                settings,
                out exitPose))
        {
            return true;
        }

        if (fallbackExits != null)
        {
            for (int i = 0; i < fallbackExits.Length; i++)
            {
                Transform fallbackExit = fallbackExits[i];

                if (fallbackExit == null ||
                    fallbackExit == primaryExit)
                {
                    continue;
                }

                if (TryResolveTransform(
                        player,
                        hidingPoint,
                        fallbackExit,
                        alignPlayerRotation,
                        settings,
                        out exitPose))
                {
                    return true;
                }
            }
        }

        if (includeRecoveryPose &&
            player.TryGetRecoveryPose(out Pose recoveryPose) &&
            IsPoseClear(
                player,
                recoveryPose,
                settings.ExitObstructionMask,
                settings.ExitTriggerInteraction,
                settings.ExitCollisionSkin))
        {
            exitPose = recoveryPose;
            return true;
        }

        return false;
    }

    internal bool TryResolveEmergency(
        PlayerHidingController player,
        Pose recoveryPose,
        Pose currentPose,
        LayerMask obstructionMask,
        QueryTriggerInteraction triggerInteraction,
        float collisionSkin,
        out Pose exitPose
    )
    {
        exitPose = default;

        if (IsPoseClear(
                player,
                recoveryPose,
                obstructionMask,
                triggerInteraction,
                collisionSkin))
        {
            exitPose = recoveryPose;
            return true;
        }

        if (IsPoseClear(
                player,
                currentPose,
                obstructionMask,
                triggerInteraction,
                collisionSkin))
        {
            exitPose = currentPose;
            return true;
        }

        for (int distanceIndex = 0;
             distanceIndex < EmergencyDistances.Length;
             distanceIndex++)
        {
            float distance = EmergencyDistances[distanceIndex];

            for (int directionIndex = 0;
                 directionIndex < EmergencyDirections.Length;
                 directionIndex++)
            {
                Pose candidate = new(
                    recoveryPose.position +
                    EmergencyDirections[directionIndex] * distance,
                    recoveryPose.rotation
                );

                if (!IsPoseClear(
                        player,
                        candidate,
                        obstructionMask,
                        triggerInteraction,
                        collisionSkin) ||
                    !IsReachable(
                        player,
                        recoveryPose,
                        candidate,
                        obstructionMask,
                        null))
                {
                    continue;
                }

                exitPose = candidate;
                return true;
            }
        }

        return false;
    }

    internal bool IsPoseClear(
        PlayerHidingController player,
        Pose pose,
        LayerMask obstructionMask,
        QueryTriggerInteraction triggerInteraction,
        float collisionSkin
    )
    {
        if (player == null ||
            obstructionMask.value == 0 ||
            !player.TryBuildExitCapsule(
                pose,
                collisionSkin,
                out Vector3 pointA,
                out Vector3 pointB,
                out float radius))
        {
            return false;
        }

        if (!Physics.CheckCapsule(
                pointA,
                pointB,
                radius,
                obstructionMask,
                triggerInteraction))
        {
            return true;
        }

        int overlapCount = Physics.OverlapCapsuleNonAlloc(
            pointA,
            pointB,
            radius,
            overlaps,
            obstructionMask,
            triggerInteraction
        );

        if (overlapCount >= overlaps.Length)
        {
            return false;
        }

        NetworkObject playerObject = player.NetworkObject;

        for (int i = 0; i < overlapCount; i++)
        {
            Collider overlap = overlaps[i];

            if (overlap == null ||
                BelongsTo(overlap, player.transform, playerObject))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private bool TryResolveTransform(
        PlayerHidingController player,
        Transform hidingPoint,
        Transform exit,
        bool alignPlayerRotation,
        HidingPlaceData settings,
        out Pose pose
    )
    {
        pose = default;

        if (exit == null)
        {
            return false;
        }

        Quaternion rotation = alignPlayerRotation
            ? exit.rotation
            : player.transform.rotation;

        Pose groundPose = new(exit.position, rotation);

        if (!player.TryBuildGroundedExitPose(
                groundPose,
                out Pose candidate))
        {
            return false;
        }

        if (!IsPoseClear(
                player,
                candidate,
                settings.ExitObstructionMask,
                settings.ExitTriggerInteraction,
                settings.ExitCollisionSkin) ||
            !IsReachable(
                player,
                hidingPoint != null
                    ? new Pose(hidingPoint.position, player.transform.rotation)
                    : new Pose(player.transform.position, player.transform.rotation),
                candidate,
                settings.ExitObstructionMask,
                hidingPoint != null
                    ? hidingPoint.GetComponentInParent<NetworkObject>()
                    : null))
        {
            return false;
        }

        pose = candidate;
        return true;
    }

    // Whether the body could get from one pose to the other in a straight
    // line. A free spot is not enough: an exit on the far side of a wall is
    // free too, and the player came out behind it. Measured from the hiding
    // point rather than from the player, whose position on the server is
    // whatever their client last reported. The place being left does not
    // stand in its own way.
    private bool IsReachable(
        PlayerHidingController player,
        Pose from,
        Pose to,
        LayerMask obstructionMask,
        NetworkObject ignoredObject
    )
    {
        if (!player.TryBuildExitCapsule(from, 0f, out Vector3 fromA, out Vector3 fromB, out _) ||
            !player.TryBuildExitCapsule(to, 0f, out Vector3 toA, out Vector3 toB, out _))
        {
            return false;
        }

        Vector3 start = (fromA + fromB) * 0.5f;
        Vector3 path = (toA + toB) * 0.5f - start;
        float distance = path.magnitude;

        if (distance <= Mathf.Epsilon)
        {
            return true;
        }

        int hitCount = Physics.RaycastNonAlloc(
            start,
            path / distance,
            hits,
            distance,
            obstructionMask,
            QueryTriggerInteraction.Ignore
        );

        if (hitCount >= hits.Length)
        {
            return false;
        }

        NetworkObject playerObject = player.NetworkObject;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hits[i].collider;

            if (hit == null ||
                BelongsTo(hit, player.transform, playerObject) ||
                (ignoredObject != null &&
                 BelongsTo(hit, ignoredObject.transform, ignoredObject)))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool BelongsTo(
        Collider candidate,
        Transform root,
        NetworkObject rootObject
    )
    {
        Transform candidateTransform = candidate.transform;

        if (candidateTransform == root ||
            candidateTransform.IsChildOf(root))
        {
            return true;
        }

        // Not asked whether it is still spawned: a place being torn down no
        // longer is, and its copies on other peers still carry its id.
        if (rootObject == null)
        {
            return false;
        }

        NetworkObject candidateNetworkObject =
            candidate.GetComponentInParent<NetworkObject>();

        return candidateNetworkObject != null &&
               candidateNetworkObject.IsSpawned &&
               candidateNetworkObject.NetworkObjectId ==
               rootObject.NetworkObjectId;
    }
}
