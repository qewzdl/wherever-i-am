using UnityEngine;

public enum EnemyHidingPlaceCheckStatus
{
    // Still worth standing here: walking over, waiting out a transition, or
    // trying the lid again.
    Checking = 0,

    // Nothing more to get from this box, for good or ill. The investigation
    // goes back to searching the room.
    Finished = 1,
}

// Knowing that people climb into boxes, and what to do about it.
//
// The first capability, and the one that made capabilities worth having. It
// used to be a third of EnemyInvestigateState - a phase, two fields, four
// methods and the longest comments in the file - and none of that is about
// searching a room, which is what the state is for. Looking for somebody and
// knowing how to open a crate are different behaviours that happen to run at
// the same moment.
//
// An enemy without this searches rooms and walks past boxes. That is a
// different enemy rather than a broken one, which is the whole test of whether
// something belongs out here: if pulling it out leaves a hole, it was not a
// capability, it was the state.
//
// Per enemy rather than per asset. The module that installs this is a shared
// ScriptableObject and must not hold a box reference; this is the object it
// makes, one for each enemy, and the mutable half lives here.
public sealed class EnemyHidingPlaceCheck
{
    private readonly EnemyBrainContext context;

    private HidingPlaceInteractable checkedHidingPlace;
    private float checkTimer;

    public EnemyHidingPlaceCheck(EnemyBrainContext context)
    {
        this.context = context;
    }

    // Whether there is a box on her mind at all. The investigation asks before
    // rebuilding its plan around a newer noise: a box somebody was watched
    // climbing into is a stronger lead than a sound, and restarting on the
    // sound would throw the stronger one away.
    public bool HasRememberedPlace =>
        context.InvestigationMemory.HasObservedHidingPlace;

    // A remembered hiding place wins over the last known position, and it has
    // to. Visual memory hands out the target's LIVE position while it runs, so
    // a target that climbed into a box leaves its last known position inside
    // that box - inside the hole the box carves out of the NavMesh. Pathing
    // there fails, the investigation gives up before it starts, and the enemy
    // forgets a player it just watched hide.
    //
    // Safe to prefer because the reference has exactly one writer: a pursued
    // target that vanished into this box while this enemy was watching.
    public bool TryGetInvestigationOrigin(out Vector3 position)
    {
        HidingPlaceInteractable hidingPlace =
            context.InvestigationMemory.ObservedHidingPlace;

        if (hidingPlace != null && hidingPlace.IsSpawned)
        {
            position = hidingPlace.EnemyInvestigationPosition;
            return true;
        }

        position = default;
        return false;
    }

    // Take on a box, if there is one worth taking on. Hands back where to walk;
    // the state owns navigation and does the walking.
    public bool TryBegin(out Vector3 approachPosition)
    {
        approachPosition = default;

        HidingPlaceInteractable hidingPlace =
            context.InvestigationMemory.ObservedHidingPlace;

        if (hidingPlace == null || !hidingPlace.IsSpawned)
        {
            context.InvestigationMemory.ClearObservedHidingPlace();
            return false;
        }

        if (hidingPlace.State != HidingTransitionState.Entering &&
            hidingPlace.State != HidingTransitionState.Occupied)
        {
            context.InvestigationMemory.ClearObservedHidingPlace();
            return false;
        }

        // No relevance check on the reference itself: it is only ever written
        // for a target this enemy watched vanish into this box, and the
        // investigation origin is that box. Enemies that saw nothing arrive
        // here with no reference at all and bail out above.
        HidingPlaceData settings = hidingPlace.Configuration;

        checkedHidingPlace = hidingPlace;

        // The budget has to pay for the walk as well as for the transition,
        // because the clock starts here and the destination is somewhere she
        // has not got to yet.
        //
        // Sized on the durations alone it came to half a second on the shipped
        // data asset - enterDuration and exitDuration are both zero there,
        // since the box has no animation to wait out - and half a second
        // expired while she was still crossing the room.
        //
        // Three times the straight line, because a route around furniture is
        // longer than the line through it. Past that she is not walking slowly,
        // she is not getting there at all, and giving up is the right answer.
        float travelDistance = Vector3.Distance(
            context.Navigator.Position,
            hidingPlace.EnemyInvestigationPosition);

        checkTimer =
            travelDistance / Mathf.Max(0.1f, context.Config.chaseSpeed) * 3f +
            (settings != null
                ? settings.EnterDuration + settings.ExitDuration
                : 0f) + 0.5f;

        approachPosition = GetApproachPosition(hidingPlace, settings);

        // Close enough already is the common case for a box watched from a
        // couple of metres, and waiting a frame to try it would be a frame of
        // standing in front of an occupied box doing nothing.
        if (hidingPlace.State == HidingTransitionState.Occupied)
        {
            TryOpen();
        }

        return true;
    }

    public EnemyHidingPlaceCheckStatus Tick(float deltaTime)
    {
        checkTimer -= Mathf.Max(0f, deltaTime);

        if (checkedHidingPlace == null ||
            !checkedHidingPlace.IsSpawned ||
            checkTimer <= 0f)
        {
            // The one place giving up actually happens, so the one place worth
            // saying why. Every field here was a guess on a previous attempt at
            // this bug: which state the box was in, how close she got, and
            // which of the five refusals was the one that kept firing.
            if (checkedHidingPlace != null)
            {
                HidingPlaceData settings = checkedHidingPlace.Configuration;

                RuntimeLog.Info(
                    "Hiding place check ran out of time in state " +
                    $"{checkedHidingPlace.State}, " +
                    $"refused as {checkedHidingPlace.LastInvestigationRefusal}, " +
                    $"at {checkedHidingPlace.LastInvestigationDistance:F2}m of " +
                    $"{(settings != null ? settings.EnemyInvestigationDistance : 0f):F2}m.");
            }

            Finish();
            return EnemyHidingPlaceCheckStatus.Finished;
        }

        if (checkedHidingPlace.State == HidingTransitionState.Entering)
        {
            return EnemyHidingPlaceCheckStatus.Checking;
        }

        // Somebody is in there and the lid would not come off this frame.
        //
        // That is not evidence the box is empty, and it used to be treated as
        // exactly that: the open is refused while she is further away than the
        // place allows, and it is refused again if the occupant has nowhere to
        // be put down - an exit point inside a wall, a doorway a dragged item
        // has closed. Any of those, on any single frame, and she forgot a box
        // she had watched somebody climb into and walked off to search the
        // room. From the player's side that is the whole bug: seen, followed,
        // and then apparently forgotten.
        //
        // So a refused open keeps her here. The wait is already bounded - the
        // timer pays for the walk and the transition and no more - and running
        // it out is the honest way to give up on a box that will not open,
        // rather than giving up on the first attempt.
        if (checkedHidingPlace.State == HidingTransitionState.Occupied)
        {
            TryOpen();
            return EnemyHidingPlaceCheckStatus.Checking;
        }

        if (checkedHidingPlace.State == HidingTransitionState.Exiting)
        {
            return EnemyHidingPlaceCheckStatus.Checking;
        }

        // Genuinely nothing in it: available, or emptied by somebody else while
        // she walked over. Worth saying out loud while this is being chased - a
        // box that reports itself free with a player inside it is a different
        // fault from the one above, in a different file.
        RuntimeLog.Info(
            "Hiding place checked and found " +
            $"{checkedHidingPlace.State} rather than occupied.");

        Finish();
        return EnemyHidingPlaceCheckStatus.Finished;
    }

    public void Reset()
    {
        checkedHidingPlace = null;
        checkTimer = 0f;
    }

    // A spot beside the anchor, on the side she is coming from, close enough
    // that the server will open the place from it.
    //
    // Six tenths of the opening distance rather than all of it: the remainder
    // is the margin that absorbs an agent stopping a little short, the radius
    // of the agent itself, and a carve that reaches slightly further than the
    // collider it was built from.
    //
    // Beside the anchor rather than on it, because every hiding place cuts a
    // hole in the NavMesh the size of its own collider the moment the server
    // spawns it, and an anchor authored at the box's centre therefore names a
    // point no agent can ever stand on. The anchor is level data: Test Hiding
    // Box has it wired to the box's own transform today, and any authored place
    // is one mis-drag away from the same dead stop.
    private Vector3 GetApproachPosition(
        HidingPlaceInteractable hidingPlace,
        HidingPlaceData settings)
    {
        Vector3 anchor = hidingPlace.EnemyInvestigationPosition;

        if (settings == null)
        {
            return anchor;
        }

        Vector3 approach = context.Navigator.Position - anchor;
        approach.y = 0f;

        // Standing exactly on top of it gives no direction to back off along.
        // Rare, and the anchor is no worse a guess than an arbitrary compass
        // point would be.
        if (approach.sqrMagnitude <= 0.0001f)
        {
            return anchor;
        }

        return anchor +
               approach.normalized *
               (settings.EnemyInvestigationDistance * 0.6f);
    }

    private bool TryOpen()
    {
        if (checkedHidingPlace == null ||
            !checkedHidingPlace.TryInvestigateServer(context.Navigator.Position))
        {
            return false;
        }

        context.InvestigationMemory.ClearObservedHidingPlace();
        return true;
    }

    // Forgetting the box is part of finishing with it, not something the caller
    // has to remember. Leaving the reference behind is what sent an enemy back
    // to the same box on the next stimulus, forever.
    private void Finish()
    {
        context.InvestigationMemory.ClearObservedHidingPlace();
        checkedHidingPlace = null;
    }
}
