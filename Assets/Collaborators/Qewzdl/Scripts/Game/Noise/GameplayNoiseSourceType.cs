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
    Footstep = 7
}