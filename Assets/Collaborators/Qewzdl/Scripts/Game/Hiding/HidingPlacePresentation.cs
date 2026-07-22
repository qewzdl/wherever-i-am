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

        if (previousState == currentState || audioSource == null)
        {
            return;
        }

        HidingPlaceData settings = hidingPlace != null
            ? hidingPlace.Configuration
            : null;
        AudioClip clip = currentState switch
        {
            HidingTransitionState.Entering =>
                settings != null ? settings.EnterSound : null,
            HidingTransitionState.Exiting =>
                settings != null ? settings.ExitSound : null,
            _ => null
        };

        if (clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    private void ApplyOccupancy(bool isOccupied)
    {
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
