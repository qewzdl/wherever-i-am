using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(HidingPlaceInteractable))]
public sealed class HidingPlacePresentation : MonoBehaviour
{
    [SerializeField] private HidingPlaceInteractable hidingPlace;
    [SerializeField] private Animator animator;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private string occupiedParameter = "IsOccupied";
    [SerializeField] private string stateParameter = "HidingState";

    private int occupiedParameterHash;
    private int stateParameterHash;
    private bool? occupied;
    private bool exitPlayed;

    // What this copy last played. There is nothing else to ask an
    // AudioSource about a one-shot once it has been fired.
    internal AudioClip LastPlayedClip { get; private set; }

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
        ApplyOccupancy(hidingPlace.IsOccupied);
        ApplyState(hidingPlace.State, hidingPlace.State);
    }

    private void OnDisable()
    {
        if (hidingPlace != null)
        {
            hidingPlace.OccupancyChanged -= ApplyOccupancy;
            hidingPlace.StateChanged -= ApplyState;
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
            Play(Settings?.ExitSound);
        }
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

        if (isOccupied)
        {
            exitPlayed = false;
            Play(Settings?.EnterSound);
            return;
        }

        if (!exitPlayed)
        {
            exitPlayed = true;
            Play(Settings?.ExitSound);
        }
    }

    private HidingPlaceData Settings =>
        hidingPlace != null ? hidingPlace.Configuration : null;

    private void Play(AudioClip clip)
    {
        if (clip == null || audioSource == null)
        {
            return;
        }

        LastPlayedClip = clip;
        audioSource.PlayOneShot(clip);
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

        if (audioSource == null)
        {
            audioSource = GetComponentInChildren<AudioSource>(true);
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
