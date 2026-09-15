// How a player is moving, as the rest of the game is allowed to know it.
//
// Three, and they are distinguished by speed alone. That is the whole point:
// speed is something a server measures for itself, so nothing here has to be
// claimed by a client and therefore nothing here can be lied about.
public enum PlayerGait
{
    // Slow enough that nothing hears it. Crouching, in this game: the quiet way
    // to move already existed, already costs a silhouette and a sightline, and
    // giving silence a second button would have made a mode that is quieter
    // than walking at no cost but time.
    Silent = 0,

    Walking = 1,

    Running = 2
}
