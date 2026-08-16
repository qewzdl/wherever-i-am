// What a search for somewhere to sneak to actually found.
//
// The planners used to answer Found/NotFound/Deferred, and the states could
// only read that as "go" or "give up". Three different situations arrived as
// NotFound - the level offers nothing reachable, everything reachable is being
// looked at, another enemy got there first, or every route was already tried -
// and they call for different answers. Being looked at is temporary and worth
// waiting out; a wall or a learned failed approach is not.
public enum EnemyStealthPlanOutcome
{
    // Somewhere to stand, by a route nobody can see the walk along.
    FoundHiddenRoute = 0,

    // Reachable, but every way there crosses somebody's view. The player is
    // doing this on purpose, and it stops the moment they look elsewhere.
    AllRoutesObserved = 1,

    // Nothing this agent can reach at all. A wall, a locked door, a drop.
    NoReachablePoint = 2,

    // Somewhere good, already claimed by another enemy.
    SlotOccupied = 3,

    // Out of query budget partway through the fan. Not a failure of the
    // behaviour and must never be counted as one: the search resumes where it
    // stopped on the next tick.
    DeferredByBudget = 4,

    // The level offers routes, but every one of them is an approach this
    // engagement has already been caught taking. Waiting for somebody to look
    // elsewhere cannot make those routes new again.
    OnlyPreviouslyFailedRoutes = 5,
}
