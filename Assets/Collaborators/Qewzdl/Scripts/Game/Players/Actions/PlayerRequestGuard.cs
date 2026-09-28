using Unity.Netcode;
using UnityEngine;

// What the server asks of a player before doing what they asked.
//
// A request arrives with nothing but who sent it, and a client can send any
// request it likes. Doors opened from the far side of the map, a handle put
// in the exit from wherever its carrier stood, and a caught player - whose
// controls are off only on their own machine - still opening, taking and
// pushing things as though they were in the match. Every one of those was a
// handler that trusted the message because an honest client would only send
// it at the right moment.
public static class PlayerRequestGuard
{
    // How far from a thing a player may be and still be believed to be using
    // it. Looser than any reach in the game, because the server sees a guest
    // where they were a moment ago, not where they are; the point is to
    // refuse the other end of the map, not half a step.
    public const float DefaultReach = 4f;

    // A player who is still in the match. Somebody without a body at all, or
    // whose body has been caught, is not.
    public static bool IsInPlay(NetworkManager manager, ulong clientId)
    {
        return TryGetPlayer(manager, clientId, out _);
    }

    public static bool IsInPlayAndNear(
        NetworkManager manager,
        ulong clientId,
        Component target,
        float reach = DefaultReach)
    {
        if (target == null || !TryGetPlayer(manager, clientId, out NetworkObject player))
            return false;

        Vector3 from = player.transform.position;
        return (ClosestPoint(target, from) - from).sqrMagnitude <= reach * reach;
    }

    private static bool TryGetPlayer(
        NetworkManager manager,
        ulong clientId,
        out NetworkObject player)
    {
        player = manager != null && manager.IsServer && manager.SpawnManager != null
            ? manager.SpawnManager.GetPlayerNetworkObject(clientId)
            : null;

        if (player == null)
            return false;

        return !player.TryGetComponent(out IPlayerInPlay inPlay) || inPlay.IsInPlay;
    }

    // To the nearest surface rather than the pivot: a door turns on its
    // hinge, and the hinge is a door's width from the handle.
    private static Vector3 ClosestPoint(Component target, Vector3 from)
    {
        Collider[] colliders = target.GetComponentsInChildren<Collider>();
        Vector3 closest = target.transform.position;
        float best = (closest - from).sqrMagnitude;

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null || !colliders[i].enabled || colliders[i].isTrigger)
                continue;

            Vector3 point = colliders[i].ClosestPoint(from);
            float distance = (point - from).sqrMagnitude;

            if (distance < best)
            {
                best = distance;
                closest = point;
            }
        }

        return closest;
    }
}
