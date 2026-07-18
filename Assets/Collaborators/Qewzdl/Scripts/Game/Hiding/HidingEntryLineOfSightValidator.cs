using Unity.Netcode;
using UnityEngine;

internal sealed class HidingEntryLineOfSightValidator
{
    private const int MaxHits = 16;

    private readonly RaycastHit[] hits = new RaycastHit[MaxHits];

    internal bool HasLineOfSight(
        PlayerHidingController player,
        HidingPlaceInteractable hidingPlace,
        Transform interactionAnchor,
        HidingPlaceData settings
    )
    {
        if (player == null ||
            hidingPlace == null ||
            interactionAnchor == null ||
            settings == null)
        {
            return false;
        }

        if (!settings.RequireEntryLineOfSight)
        {
            return true;
        }

        if (settings.EntryLineOfSightBlockingMask.value == 0)
        {
            return false;
        }

        Vector3 origin = player.EntryValidationOrigin;
        Vector3 direction = interactionAnchor.position - origin;
        float distance = direction.magnitude;

        if (distance <= Mathf.Epsilon)
        {
            return true;
        }

        direction /= distance;

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            direction,
            hits,
            distance,
            settings.EntryLineOfSightBlockingMask,
            settings.EntryLineOfSightTriggerInteraction
        );

        if (hitCount >= hits.Length)
        {
            return false;
        }

        NetworkObject playerObject = player.NetworkObject;
        NetworkObject hidingObject = hidingPlace.NetworkObject;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = hits[i].collider;

            if (hitCollider == null ||
                BelongsTo(hitCollider, player.transform, playerObject) ||
                BelongsTo(hitCollider, hidingPlace.transform, hidingObject))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool BelongsTo(
        Collider candidate,
        Transform hierarchyRoot,
        NetworkObject expectedNetworkObject
    )
    {
        Transform candidateTransform = candidate.transform;

        if (candidateTransform == hierarchyRoot ||
            candidateTransform.IsChildOf(hierarchyRoot))
        {
            return true;
        }

        if (expectedNetworkObject == null ||
            !expectedNetworkObject.IsSpawned)
        {
            return false;
        }

        NetworkObject candidateNetworkObject =
            candidate.GetComponentInParent<NetworkObject>();

        return candidateNetworkObject != null &&
               candidateNetworkObject.IsSpawned &&
               candidateNetworkObject.NetworkObjectId ==
               expectedNetworkObject.NetworkObjectId;
    }
}
