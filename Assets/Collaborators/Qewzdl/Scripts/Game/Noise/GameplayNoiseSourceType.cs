public enum GameplayNoiseSourceType
{
    Unknown = 0,
    Door = 1,
    Item = 2,
    Environment = 3,
    Trap = 4,
    Npc = 5,
    Player = 6,

    // Separate from Player on purpose. Everything else a player emits is an
    // event - a box lid, a phone, a dropped tin - and an enemy may react to
    // those out loud. A walk is a rhythm, and something that exclaims every
    // few seconds for as long as anybody is moving stops being a reaction.
    Footstep = 7,

    // Quiet, constant while somebody is winded, and for the same reason as
    // Footstep it is not worth exclaiming at: a reaction that repeats for as
    // long as a player is out of breath stops being a reaction.
    //
    // A cough is not this. A cough is one event, loud and involuntary, and it
    // goes out as Player - the kind she is allowed to notice out loud.
    Breath = 8
}