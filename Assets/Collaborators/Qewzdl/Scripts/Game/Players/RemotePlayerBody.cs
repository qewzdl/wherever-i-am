using Unity.Netcode;
using UnityEngine;

// Somebody else's body is not simulated here.
//
// Every machine used to keep every player as a full physics body - gravity,
// mass, collisions - while the network moved the ones it did not own by
// setting their position every frame. The two fought. On the host a guest's
// copy was placed into the host's body and physics shoved the host out of the
// way; on the guest the host's copy was shoved and put straight back. So a
// guest could push the host and never the other way round, and the enemy on
// the server could push the host and only jostled a guest's copy.
//
// Kinematic, those copies are where the network says and nowhere else, and
// every body meets every other one the same way from both sides. Not
// interpolated by physics either: the network already smooths them, and the
// Rigidbody's own interpolation would take back positions it did not write.
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class RemotePlayerBody : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        Rigidbody body = GetComponent<Rigidbody>();

        // Every player's body, own or not, is one the enemy cannot push.
        EnemyPlayerContacts.AddPlayer(body);

        if (IsOwner)
            return;

        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.None;
    }

    public override void OnNetworkDespawn()
    {
        EnemyPlayerContacts.RemovePlayer(GetComponent<Rigidbody>());
    }
}
