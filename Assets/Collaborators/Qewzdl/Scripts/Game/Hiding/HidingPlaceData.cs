using UnityEngine;

[CreateAssetMenu(
    fileName = "NewHidingPlaceData",
    menuName = "Wherever I Am/Items/Hiding Place data"
)]
public sealed class HidingPlaceData : InteractableObjectData
{
    [Header("Interaction")]
    [SerializeField, Min(0.1f)] private float maxInteractionDistance = 2.5f;
    [SerializeField] private bool requireEntryLineOfSight = true;
    [SerializeField] private LayerMask entryLineOfSightBlockingMask = ~0;
    [SerializeField] private QueryTriggerInteraction entryLineOfSightTriggerInteraction =
        QueryTriggerInteraction.Ignore;

    [Header("Hidden Player")]
    [SerializeField] private bool hidePlayerVisuals = true;
    [SerializeField] private bool disablePlayerColliders = true;
    [SerializeField] private bool alignPlayerRotation = true;

    [Header("Safe Exit")]
    [SerializeField] private LayerMask exitObstructionMask = ~0;
    [SerializeField] private QueryTriggerInteraction exitTriggerInteraction =
        QueryTriggerInteraction.Ignore;
    [SerializeField, Min(0f)] private float exitCollisionSkin = 0.02f;

    public float MaxInteractionDistance => Mathf.Max(0.1f, maxInteractionDistance);
    public bool RequireEntryLineOfSight => requireEntryLineOfSight;
    public LayerMask EntryLineOfSightBlockingMask =>
        entryLineOfSightBlockingMask;
    public QueryTriggerInteraction EntryLineOfSightTriggerInteraction =>
        entryLineOfSightTriggerInteraction;
    public bool HidePlayerVisuals => hidePlayerVisuals;
    public bool DisablePlayerColliders => disablePlayerColliders;
    public bool AlignPlayerRotation => alignPlayerRotation;
    public LayerMask ExitObstructionMask => exitObstructionMask;
    public QueryTriggerInteraction ExitTriggerInteraction =>
        exitTriggerInteraction;
    public float ExitCollisionSkin => Mathf.Max(0f, exitCollisionSkin);

#if UNITY_EDITOR
    private void OnValidate()
    {
        maxInteractionDistance = Mathf.Max(0.1f, maxInteractionDistance);
        exitCollisionSkin = Mathf.Max(0f, exitCollisionSkin);
    }
#endif
}
