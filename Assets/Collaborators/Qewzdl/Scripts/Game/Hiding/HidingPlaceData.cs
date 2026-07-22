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
    [SerializeField] private HidingPoseType hidingPose = HidingPoseType.Standing;

    [Header("Transition")]
    [SerializeField, Min(0f)] private float enterDuration;
    [SerializeField, Min(0f)] private float exitDuration;

    [Header("Camera")]
    [SerializeField] private bool allowPeeking = true;
    [SerializeField, Range(-180f, 0f)] private float minimumCameraYaw = -55f;
    [SerializeField, Range(0f, 180f)] private float maximumCameraYaw = 55f;
    [SerializeField, Range(-89f, 0f)] private float minimumCameraPitch = -35f;
    [SerializeField, Range(0f, 89f)] private float maximumCameraPitch = 45f;

    [Header("Hiding Feedback")]
    [SerializeField] private bool showHidingVignette = true;
    [SerializeField] private Color hidingVignetteColor = Color.black;
    [SerializeField, Range(0f, 1f)] private float hidingVignetteOpacity = 0.65f;
    [SerializeField, Range(0f, 0.95f)] private float hidingVignetteInnerRadius = 0.4f;
    [SerializeField, Min(0f)] private float hidingVignetteFadeDuration = 0.2f;

    [Header("Presentation Audio")]
    [SerializeField] private AudioClip enterSound;
    [SerializeField] private AudioClip exitSound;

    [Header("Gameplay Noise")]
    [SerializeField, Min(0f)] private float enterNoiseRadius = 6f;
    [SerializeField, Min(0f)] private float enterNoiseLoudness = 0.7f;
    [SerializeField, Min(0f)] private float exitNoiseRadius = 5f;
    [SerializeField, Min(0f)] private float exitNoiseLoudness = 0.55f;

    [Header("Enemy Investigation")]
    [SerializeField] private bool enemiesCanInvestigate = true;
    [SerializeField, Min(0.1f)] private float enemyInvestigationDistance = 2.25f;

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
    public HidingPoseType HidingPose => hidingPose;
    public float EnterDuration => Mathf.Max(0f, enterDuration);
    public float ExitDuration => Mathf.Max(0f, exitDuration);
    public bool AllowPeeking => allowPeeking;
    public float MinimumCameraYaw => Mathf.Min(0f, minimumCameraYaw);
    public float MaximumCameraYaw => Mathf.Max(0f, maximumCameraYaw);
    public float MinimumCameraPitch => Mathf.Min(0f, minimumCameraPitch);
    public float MaximumCameraPitch => Mathf.Max(0f, maximumCameraPitch);
    public bool ShowHidingVignette => showHidingVignette;
    public Color HidingVignetteColor => hidingVignetteColor;
    public float HidingVignetteOpacity =>
        Mathf.Clamp01(hidingVignetteOpacity);
    public float HidingVignetteInnerRadius =>
        Mathf.Clamp(hidingVignetteInnerRadius, 0f, 0.95f);
    public float HidingVignetteFadeDuration =>
        Mathf.Max(0f, hidingVignetteFadeDuration);
    public AudioClip EnterSound => enterSound;
    public AudioClip ExitSound => exitSound;
    public float EnterNoiseRadius => Mathf.Max(0f, enterNoiseRadius);
    public float EnterNoiseLoudness => Mathf.Max(0f, enterNoiseLoudness);
    public float ExitNoiseRadius => Mathf.Max(0f, exitNoiseRadius);
    public float ExitNoiseLoudness => Mathf.Max(0f, exitNoiseLoudness);
    public bool EnemiesCanInvestigate => enemiesCanInvestigate;
    public float EnemyInvestigationDistance =>
        Mathf.Max(0.1f, enemyInvestigationDistance);
    public LayerMask ExitObstructionMask => exitObstructionMask;
    public QueryTriggerInteraction ExitTriggerInteraction =>
        exitTriggerInteraction;
    public float ExitCollisionSkin => Mathf.Max(0f, exitCollisionSkin);

#if UNITY_EDITOR
    private void OnValidate()
    {
        maxInteractionDistance = Mathf.Max(0.1f, maxInteractionDistance);
        enterDuration = Mathf.Max(0f, enterDuration);
        exitDuration = Mathf.Max(0f, exitDuration);
        minimumCameraYaw = Mathf.Clamp(minimumCameraYaw, -180f, 0f);
        maximumCameraYaw = Mathf.Clamp(maximumCameraYaw, 0f, 180f);
        minimumCameraPitch = Mathf.Clamp(minimumCameraPitch, -89f, 0f);
        maximumCameraPitch = Mathf.Clamp(maximumCameraPitch, 0f, 89f);
        hidingVignetteOpacity = Mathf.Clamp01(hidingVignetteOpacity);
        hidingVignetteInnerRadius = Mathf.Clamp(
            hidingVignetteInnerRadius,
            0f,
            0.95f
        );
        hidingVignetteFadeDuration = Mathf.Max(
            0f,
            hidingVignetteFadeDuration
        );
        enterNoiseRadius = Mathf.Max(0f, enterNoiseRadius);
        enterNoiseLoudness = Mathf.Max(0f, enterNoiseLoudness);
        exitNoiseRadius = Mathf.Max(0f, exitNoiseRadius);
        exitNoiseLoudness = Mathf.Max(0f, exitNoiseLoudness);
        enemyInvestigationDistance =
            Mathf.Max(0.1f, enemyInvestigationDistance);
        exitCollisionSkin = Mathf.Max(0f, exitCollisionSkin);
    }
#endif
}
