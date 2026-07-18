using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(HidingPlaceInteractable))]
public sealed class HidingPlacePresentation : MonoBehaviour
{
    [SerializeField] private HidingPlaceInteractable hidingPlace;
    [SerializeField] private Animator animator;
    [SerializeField] private string occupiedParameter = "IsOccupied";

    private int occupiedParameterHash;

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
        ApplyOccupancy(hidingPlace.IsOccupied);
    }

    private void OnDisable()
    {
        if (hidingPlace != null)
        {
            hidingPlace.OccupancyChanged -= ApplyOccupancy;
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
    }

    private void CacheParameterHash()
    {
        occupiedParameterHash = string.IsNullOrWhiteSpace(
            occupiedParameter
        )
            ? 0
            : Animator.StringToHash(occupiedParameter);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveReferences();
        CacheParameterHash();
    }
#endif
}
