using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(HidingPlaceInteractable))]
public sealed class HidingPlacePresentation : MonoBehaviour
{
    [SerializeField] private HidingPlaceInteractable hidingPlace;
    [SerializeField] private Animator animator;

    // Heard by everybody, from the place itself. Played through the gameplay
    // sounds like every other sound in the world, so the effects volume, the
    // mixer and the distance a sound carries apply to it; a source of its own
    // on the prefab had none of that, and on the one prefab there was, no
    // source at all - so nothing was ever heard.
    [SerializeField] private SoundEffect enterSound;
    [SerializeField] private SoundEffect exitSound;
    [SerializeField] private string occupiedParameter = "IsOccupied";
    [SerializeField] private string stateParameter = "HidingState";

    private int occupiedParameterHash;
    private int stateParameterHash;
    private bool? occupied;
    private bool exitPlayed;

    private const uint EnterSlot = 0u;
    private const uint ExitSlot = 1u;

    private IGameplaySoundService gameplaySound;

    // The entry this machine has already sounded, ahead of the server, for
    // its own player climbing in.
    private ulong soundedEarlyFor = HidingPlaceInteractable.NoOccupantNetworkObjectId;
    private uint soundedEarlyEntry;

    // What this copy last played, and which take of it. There is nothing to
    // ask a fired one-shot afterwards.
    internal SoundEffect LastPlayedSound { get; private set; }
    internal AudioClip LastPlayedClip { get; private set; }
    internal int PlayCount { get; private set; }

    private void Awake()
    {
        ResolveReferences();
        CacheParameterHash();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheParameterHash();

        if (hidingPlace == null)
        {
            return;
        }

        hidingPlace.OccupancyChanged += ApplyOccupancy;
        hidingPlace.StateChanged += ApplyState;
        hidingPlace.EntryRequestedLocally += SoundEntryEarly;
        ApplyOccupancy(hidingPlace.IsOccupied);
        ApplyState(hidingPlace.State, hidingPlace.State);
    }

    private void OnDisable()
    {
        if (hidingPlace != null)
        {
            hidingPlace.OccupancyChanged -= ApplyOccupancy;
            hidingPlace.StateChanged -= ApplyState;
            hidingPlace.EntryRequestedLocally -= SoundEntryEarly;
        }
    }

    private void ApplyState(
        HidingTransitionState previousState,
        HidingTransitionState currentState
    )
    {
        if (animator != null && stateParameterHash != 0)
        {
            animator.SetInteger(
                stateParameterHash,
                (int)currentState
            );
        }

        // Climbing out is heard when it starts, where it takes long enough
        // to be seen; see ApplyOccupancy for when it does not.
        if (previousState != currentState &&
            currentState == HidingTransitionState.Exiting &&
            !exitPlayed)
        {
            exitPlayed = true;
            Play(exitSound, ExitSlot, hidingPlace.Entries);
        }
    }

    // Climbing in is instant, so on the climber's own machine the sound used
    // to wait for the server's answer and arrive with the camera already
    // inside - a beat late, and to a guest the whole round trip late. The
    // climber hears it on asking instead, as the next entry the server will
    // count, which is the take everybody else is about to hear.
    //
    // Only climbing in. A request to climb out is refused while every exit is
    // blocked, and sounding it early would be a sound for nothing on every
    // press.
    private void SoundEntryEarly(ulong playerNetworkObjectId)
    {
        exitPlayed = false;
        soundedEarlyFor = playerNetworkObjectId;
        soundedEarlyEntry = hidingPlace.Entries + 1u;
        Play(enterSound, EnterSlot, soundedEarlyEntry);
    }

    // The sounds hang off who is inside rather than off the climbing.
    //
    // With no time given to climbing in or out, the place is Entering and
    // Occupied - or Exiting and Available - within one call on the server.
    // Only where it ended up reaches anybody else, so a guest never saw either
    // climb, and the host, whose copy is the one being changed, heard every
    // sound nobody else did. Somebody being inside, and then not, reaches
    // everyone whatever the timings.
    private void PlayForOccupancyChange(bool isOccupied)
    {
        bool? wasOccupied = occupied;
        occupied = isOccupied;

        if (wasOccupied == null || wasOccupied == isOccupied)
        {
            return;
        }

        bool soundedEarly =
            isOccupied &&
            hidingPlace.OccupantNetworkObjectId == soundedEarlyFor &&
            hidingPlace.Entries == soundedEarlyEntry;
        soundedEarlyFor = HidingPlaceInteractable.NoOccupantNetworkObjectId;

        if (isOccupied)
        {
            exitPlayed = false;

            if (!soundedEarly)
                Play(enterSound, EnterSlot, hidingPlace.Entries);

            return;
        }

        if (!exitPlayed)
        {
            exitPlayed = true;
            Play(exitSound, ExitSlot, hidingPlace.Entries);
        }
    }

    // The same take on every machine: rolled from this place and which entry
    // into it this is (SoundRoll).
    private void Play(SoundEffect sound, uint slot, uint entry)
    {
        if (sound == null || hidingPlace == null)
        {
            return;
        }

        SoundRoll roll = SoundRoll.For(hidingPlace.NetworkObjectId, entry, slot);

        LastPlayedSound = sound;
        LastPlayedClip = sound.GetClip(roll);
        PlayCount++;

        gameplaySound ??= AudioServices.Gameplay();
        gameplaySound?.PlayAtPosition(sound, transform.position, roll);
    }

    private void ApplyOccupancy(bool isOccupied)
    {
        PlayForOccupancyChange(isOccupied);

        if (animator == null || occupiedParameterHash == 0)
        {
            return;
        }

        animator.SetBool(occupiedParameterHash, isOccupied);
    }

    private void ResolveReferences()
    {
        if (hidingPlace == null)
        {
            hidingPlace = GetComponent<HidingPlaceInteractable>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }
    }

    private void CacheParameterHash()
    {
        occupiedParameterHash = string.IsNullOrWhiteSpace(
            occupiedParameter
        )
            ? 0
            : Animator.StringToHash(occupiedParameter);
        stateParameterHash = string.IsNullOrWhiteSpace(
            stateParameter
        )
            ? 0
            : Animator.StringToHash(stateParameter);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveReferences();
        CacheParameterHash();
    }
#endif
}
