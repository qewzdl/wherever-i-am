using UnityEngine;

[CreateAssetMenu(
    fileName = "NewHidingPlaceData",
    menuName = "Wherever I Am/Items/Hiding Place data"
)]
public sealed class HidingPlaceData : InteractableObjectData
{
    [Header("Interaction")]
    [SerializeField, Min(0.1f)] private float maxInteractionDistance = 2.5f;

    [Header("Hidden Player")]
    [SerializeField] private bool hidePlayerVisuals = true;
    [SerializeField] private bool disablePlayerColliders = true;
    [SerializeField] private bool alignPlayerRotation = true;

    public float MaxInteractionDistance => Mathf.Max(0.1f, maxInteractionDistance);
    public bool HidePlayerVisuals => hidePlayerVisuals;
    public bool DisablePlayerColliders => disablePlayerColliders;
    public bool AlignPlayerRotation => alignPlayerRotation;

#if UNITY_EDITOR
    private void OnValidate()
    {
        maxInteractionDistance = Mathf.Max(0.1f, maxInteractionDistance);
    }
#endif
}
