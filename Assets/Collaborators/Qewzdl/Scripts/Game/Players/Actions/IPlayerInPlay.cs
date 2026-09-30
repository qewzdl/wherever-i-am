using UnityEngine;

// Whether a player is still in the match - the one answer every system asks
// for, rather than one it is told.
//
// Being caught used to be announced: the attack receiver went down a list,
// telling the enemy's target to stop being seen and the gaze to stop
// watching, while the noise, the breathing and the spectator view each asked
// the receiver whether it had been caught. Two sources, and a list that any
// new way out of the match would have had to be added to by hand. Everything
// asks this now, so whatever takes a player out of play in future only has
// to change the answer.
public interface IPlayerInPlay
{
    bool IsInPlay { get; }
}

public static class PlayerInPlay
{
    // True for anything that is not part of a player, so a component can ask
    // without knowing whether it is on one.
    public static bool Of(Component component)
    {
        if (component == null)
            return true;

        IPlayerInPlay player = component.GetComponentInParent<IPlayerInPlay>();
        return player == null || player.IsInPlay;
    }
}
