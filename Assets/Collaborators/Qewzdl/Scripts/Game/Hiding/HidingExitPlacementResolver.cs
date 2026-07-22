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

    internal bool TryResolve(
        PlayerHidingController player,
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
                        collisionSkin))
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
                BelongsToPlayer(overlap, player, playerObject))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private bool TryResolveTransform(
        PlayerHidingController player,
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
                settings.ExitCollisionSkin))
        {
            return false;
        }

        pose = candidate;
        return true;
    }

    private static bool BelongsToPlayer(
        Collider candidate,
        PlayerHidingController player,
        NetworkObject playerObject
    )
    {
        Transform candidateTransform = candidate.transform;

        if (candidateTransform == player.transform ||
            candidateTransform.IsChildOf(player.transform))
        {
            return true;
        }

        if (playerObject == null || !playerObject.IsSpawned)
        {
            return false;
        }

        NetworkObject candidateNetworkObject =
            candidate.GetComponentInParent<NetworkObject>();

        return candidateNetworkObject != null &&
               candidateNetworkObject.IsSpawned &&
               candidateNetworkObject.NetworkObjectId ==
               playerObject.NetworkObjectId;
    }
}
