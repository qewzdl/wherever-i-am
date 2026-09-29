using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

// The enemy cannot shove a player; a player is a wall to it.
//
// On the server a guest's body is kinematic - somebody else's body, which
// nothing here moves (RemotePlayerBody) - so the enemy already stopped
// against guests. The host's own body is a real one, a sixth of the enemy's
// weight, and the enemy walked it out of the way. Every contact between the
// two is changed here so the player's side weighs nothing to move: the enemy
// stops against the host exactly as it does against a guest, and nobody is
// pushed, on anybody's machine.
public static class EnemyPlayerContacts
{
    private static readonly HashSet<int> PlayerBodies = new();
    private static readonly HashSet<int> EnemyBodies = new();
    private static bool listening;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay()
    {
        PlayerBodies.Clear();
        EnemyBodies.Clear();

        if (!listening)
            return;

        Physics.ContactModifyEvent -= MakePlayersImmovable;
        Physics.ContactModifyEventCCD -= MakePlayersImmovable;
        listening = false;
    }

    public static void AddPlayer(Rigidbody body)
    {
        if (body == null)
            return;

        PlayerBodies.Add(body.GetInstanceID());
        Listen();
    }

    public static void RemovePlayer(Rigidbody body)
    {
        if (body != null)
            PlayerBodies.Remove(body.GetInstanceID());
    }

    // Contacts are only offered for changing on colliders that ask for it,
    // so the enemy's own are marked here.
    public static void AddEnemy(Rigidbody body)
    {
        if (body == null)
            return;

        EnemyBodies.Add(body.GetInstanceID());

        foreach (Collider collider in body.GetComponentsInChildren<Collider>(true))
            collider.hasModifiableContacts = true;

        Listen();
    }

    public static void RemoveEnemy(Rigidbody body)
    {
        if (body != null)
            EnemyBodies.Remove(body.GetInstanceID());
    }

    private static void Listen()
    {
        if (listening)
            return;

        Physics.ContactModifyEvent += MakePlayersImmovable;
        Physics.ContactModifyEventCCD += MakePlayersImmovable;
        listening = true;
    }

    // Called by the physics step, possibly off the main thread - it only
    // reads the two sets, which change on the main thread between steps.
    private static void MakePlayersImmovable(
        PhysicsScene scene,
        NativeArray<ModifiableContactPair> pairs)
    {
        for (int i = 0; i < pairs.Length; i++)
        {
            ModifiableContactPair pair = pairs[i];
            int body = pair.bodyInstanceID;
            int other = pair.otherBodyInstanceID;

            bool playerIsOther = EnemyBodies.Contains(body) && PlayerBodies.Contains(other);
            bool playerIsFirst = PlayerBodies.Contains(body) && EnemyBodies.Contains(other);

            if (!playerIsOther && !playerIsFirst)
                continue;

            ModifiableMassProperties mass = pair.massProperties;

            if (playerIsOther)
            {
                mass.otherInverseMassScale = 0f;
                mass.otherInverseInertiaScale = 0f;
            }
            else
            {
                mass.inverseMassScale = 0f;
                mass.inverseInertiaScale = 0f;
            }

            pair.massProperties = mass;
            pairs[i] = pair;
        }
    }
}
