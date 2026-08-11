using UnityEngine;

// Decides whether an enemy is allowed to change who it is dealing with.
//
// Perception used to answer that on its own, in two places that did not agree.
// The vision-only path checked allowSwitchToNewVisibleTarget; the path taken
// when a noise arrived in the same tick did not check anything and simply
// handed over whichever target vision scored highest. Two players in a room
// were enough for an enemy to swap between them every refresh, and the attack
// pipeline kept swinging at whoever it had started on, so the replicated
// target and the one being hit were different people.
//
// One gate, consulted after the stimulus is resolved and before the target
// reaches the blackboard, so every path goes through the same rules.
public sealed class EnemyTargetSelector
{
    private float committedAtTime = float.NegativeInfinity;
    private EnemyTarget committedTarget;
    private bool hasCommitment;

    public EnemyTarget CommittedTarget => committedTarget;

    // A state that has already built a plan around its target. Changing it
    // halfway means an approach walked for someone else, a wind-up landing on
    // a third party, or an ambush sprung on nobody.
    //
    // The whole stealth manoeuvre counts, not just its last two phases. Stalk
    // and Retreat were outside the lock, so a manoeuvre could change person
    // between watching someone and backing off, and then flank the new one
    // with a plan built for the old.
    public static bool IsTargetLockedBy(EnemyState state)
    {
        return state == EnemyState.Attack ||
               EnemyStateRules.IsStealthManeuver(state);
    }

    // The explicit abort. Only leaving the manoeuvre - or the pursuit ending -
    // releases the lock; a perception refresh never does.
    public void Clear()
    {
        committedTarget = null;
        committedAtTime = float.NegativeInfinity;
        hasCommitment = false;
    }

    // currentScore is only meaningful when hasCurrentScore is set: a target
    // that cannot be seen at all has no score to defend with, and holding on
    // to it against something in plain view is how an enemy ends up guarding a
    // wall.
    public bool ShouldSwitchTo(
        EnemyTarget currentTarget,
        EnemyTarget candidate,
        float candidateScore,
        float currentScore,
        bool hasCurrentScore,
        EnemyState currentState,
        float serverTime,
        bool allowSwitchToNewVisibleTarget,
        float minimumHoldDuration,
        float requiredScoreAdvantage
    )
    {
        if (candidate == null)
        {
            return false;
        }

        // Asked before "is there anything to defend", and that order is the
        // whole point. Perception hands this a null current target whenever
        // the held one stops being valid - despawned, disconnected, climbed
        // into a box - and the branch below took that as permission to adopt
        // whoever else was in view. A locked state then carried on with a plan
        // built around somebody who is no longer in the level, aimed at
        // somebody who never entered it. The target going away is a reason for
        // the manoeuvre to abort, and aborting is what releases the lock.
        if (IsTargetLockedBy(currentState) &&
            hasCommitment &&
            !IsCommittedTo(candidate))
        {
            return false;
        }

        // Nothing to defend. Anything visible beats nothing.
        if (currentTarget == null || currentTarget == candidate)
        {
            return true;
        }

        if (!allowSwitchToNewVisibleTarget)
        {
            return false;
        }

        if (IsTargetLockedBy(currentState))
        {
            return false;
        }

        if (serverTime - committedAtTime < Mathf.Max(0f, minimumHoldDuration))
        {
            return false;
        }

        if (!hasCurrentScore)
        {
            return true;
        }

        return candidateScore >=
               currentScore * Mathf.Max(1f, requiredScoreAdvantage);
    }

    // Called once the switch has actually been applied, so the hold timer
    // measures time on a target rather than time since the last time anything
    // was considered.
    public void NotifyCommitted(EnemyTarget target, float serverTime)
    {
        if (target == null)
        {
            Clear();
            return;
        }

        if (committedTarget == target)
        {
            return;
        }

        committedTarget = target;
        committedAtTime = serverTime;
        hasCommitment = true;
    }

    // Unity's destroyed-object equality on purpose: a commitment whose object
    // has gone matches nothing, so no live candidate can inherit the lock by
    // being the only one left standing.
    private bool IsCommittedTo(EnemyTarget candidate)
    {
        return committedTarget != null && committedTarget == candidate;
    }
}
