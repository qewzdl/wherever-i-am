using UnityEngine;

public enum EnemyStealthManeuverStatus
{
    // Still a manoeuvre against the person it started against.
    Running = 0,

    // Nothing recent enough to plan with. Whatever was observed is old enough
    // that the target could be anywhere; the honest next move is to search.
    ObservationLost = 1,

    // Long enough. Sneaking has had its chance and produced nothing.
    Expired = 2,
}

// The stealth manoeuvre, as one thing.
//
// Stalk, Retreat, Flank and Ambush are four EnemyStates because presentation
// needs four of them. Underneath they are one behaviour with one victim, one
// observation of that victim and one deadline - and each handler used to keep
// its own copy of all three. Two consequences, both seen in play: the
// manoeuvre could quietly change person between phases, and a Retreat handing
// to a Flank handing back to a Retreat reset every timer it had, so an
// observation taken half a minute ago kept being planned against because
// nothing ever looked at EnemyTargetObservation.ServerTime.
//
// Phases still own their own pacing - a flank timeout is a flank's business -
// but the target, the pose being planned against and the overall budget are
// held here and read by all four.
public sealed class EnemyStealthManeuver
{
    public bool IsActive { get; private set; }
    public EnemyState Phase { get; private set; } = EnemyState.Idle;
    public EnemyTargetObservation Observation { get; private set; }
    public float StartedAt { get; private set; }

    // How many times the manoeuvre has moved between phases. Retreat and Flank
    // can hand back and forth indefinitely while the level offers no way
    // round, and the count is what makes that visible to the deadline.
    public int PhaseChanges { get; private set; }

    public EnemyTargetIdentity LockedTarget => Observation.TargetIdentity;

    public void Begin(
        EnemyTargetObservation observation,
        EnemyState phase,
        float serverTime
    )
    {
        IsActive = observation.IsValid;
        Observation = observation;
        Phase = phase;
        StartedAt = serverTime;
        PhaseChanges = 0;
    }

    public void EnterPhase(EnemyState phase)
    {
        if (!IsActive || Phase == phase)
        {
            return;
        }

        Phase = phase;
        PhaseChanges++;
    }

    // Perception saw someone. Accepted only if it is the same person this
    // manoeuvre committed to - otherwise a second player walking past would
    // move the destination the enemy is creeping towards.
    public bool TryRefresh(EnemyTargetObservation observation)
    {
        if (!IsActive || !observation.IsValid || !IsSameTargetAs(observation))
        {
            return false;
        }

        Observation = observation;
        return true;
    }

    private bool IsSameTargetAs(EnemyTargetObservation candidate)
    {
        if (!Observation.IsValid || !candidate.IsValid)
        {
            return false;
        }

        // While the observed object is alive, reference equality settles it.
        // Once it has gone, the replicated identity captured at observation
        // time is the only thing left that names a person.
        if (Observation.Target != null)
        {
            return Observation.Target == candidate.Target;
        }

        return Observation.TargetIdentity.HasTarget &&
               Observation.TargetIdentity.Equals(candidate.TargetIdentity);
    }

    public float ObservationAge(float serverTime)
    {
        return Observation.IsValid
            ? serverTime - Observation.ServerTime
            : float.PositiveInfinity;
    }

    public EnemyStealthManeuverStatus Evaluate(
        float serverTime,
        float observationMaxAge,
        float maneuverBudget,
        out EnemyTargetObservation observation
    )
    {
        observation = Observation;

        if (!IsActive || !Observation.IsValid)
        {
            return EnemyStealthManeuverStatus.ObservationLost;
        }

        if (ObservationAge(serverTime) > Mathf.Max(0.1f, observationMaxAge))
        {
            return EnemyStealthManeuverStatus.ObservationLost;
        }

        if (serverTime - StartedAt > Mathf.Max(1f, maneuverBudget))
        {
            return EnemyStealthManeuverStatus.Expired;
        }

        return EnemyStealthManeuverStatus.Running;
    }

    public void End()
    {
        IsActive = false;
        Observation = default;
        Phase = EnemyState.Idle;
        StartedAt = 0f;
        PhaseChanges = 0;
    }
}
