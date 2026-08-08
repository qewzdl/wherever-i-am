public enum EnemyState
{
    Idle = 0,
    Patrol = 1,
    Chase = 2,
    Attack = 3,
    Investigate = 4,

    // Seen the target from far enough away to be worth going round rather
    // than straight at it.
    Stalk = 5,

    // Been noticed back, and breaking off until out of sight.
    Retreat = 6,

    // Out of sight, walking round to come at the target from behind.
    Flank = 7,

    // In place behind the target, waiting for it to turn round.
    Ambush = 8
}