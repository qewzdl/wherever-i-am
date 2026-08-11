using System.Collections.Generic;
using UnityEngine;

// Who has claimed which spot to stand in.
//
// Two enemies both work out "behind the player, three and a half metres" and
// walk into each other. Whoever gets there first holds the spot; the other is
// pushed on to the next candidate in its fan.
//
// Keyed by the enemy's scheduler id rather than by the state handler object,
// so a claim can be released from anywhere that knows the enemy - a handler
// that is torn down mid-manoeuvre used to leave its point reserved for the
// rest of the match.
public static class EnemyTacticalSlotRegistry
{
    private static readonly Dictionary<int, Vector3> Claims = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetClaims()
    {
        Claims.Clear();
    }

    public static void Claim(int ownerId, Vector3 point)
    {
        Claims[ownerId] = point;
    }

    // Look and take in one go.
    //
    // A search that spans several ticks found a free spot, kept it as its
    // fallback and claimed it a second later without looking again - and in
    // that second the enemy next to it had walked its own fan, found the same
    // gap behind the same player and claimed it. Both then walked to it.
    public static bool TryClaim(int ownerId, Vector3 point, float spacing)
    {
        if (IsClaimedByAnother(ownerId, point, spacing))
        {
            return false;
        }

        Claims[ownerId] = point;
        return true;
    }

    public static void Release(int ownerId)
    {
        Claims.Remove(ownerId);
    }

    public static bool IsClaimedByAnother(
        int ownerId,
        Vector3 point,
        float spacing
    )
    {
        float sqrSpacing = spacing * spacing;

        foreach (KeyValuePair<int, Vector3> claim in Claims)
        {
            if (claim.Key != ownerId &&
                (claim.Value - point).sqrMagnitude < sqrSpacing)
            {
                return true;
            }
        }

        return false;
    }

    public static void ResetForTests()
    {
        Claims.Clear();
    }
}
