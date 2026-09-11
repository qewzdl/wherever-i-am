using UnityEngine;

// Viewmodel sway: the held item follows the head with a lag, so it reads as a hand with
// some mass rather than a decal pinned to the screen. Two sources feed it - the summed
// camera effects (bob, breathing, roll, shake) from PlayerCameraEffects, and the head turn
// rate, which throws the item a little the way the hand trails a fast look.
//
// This writes to the viewmodel overlay camera, not to ItemScene. ItemScene parents both
// the item and that camera, so moving it moves both and nothing changes on screen. Moving
// the overlay camera by a fraction of the main camera's offset drifts the item on screen by
// that fraction, in the same direction the world moves, one beat behind.
//
// Ordered after PlayerCameraEffects so LastOutput and the turn rates are this frame's.
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class ViewmodelSway : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerCameraEffects cameraEffects;
    [Tooltip("The overlay camera that renders the held item (child of ItemScene).")]
    [SerializeField] private Transform viewmodelCamera;

    [Header("Strength")]
    [Tooltip("How much of the head's motion reaches the item (0 = item is still, 1 = authored, 5 = five times). Scales everything below.")]
    [SerializeField, Range(0f, 5f)] private float strength = 1f;

    [Header("Follow (bob, breathing, roll, shake)")]
    [Tooltip("How much of the camera's sideways travel (strafe sway, side-to-side bob) the item follows.")]
    [SerializeField, Range(0f, 5f)] private float positionStrengthX = 0.5f;
    [Tooltip("How much of the camera's vertical travel (footfall dip, breathing) the item follows.")]
    [SerializeField, Range(0f, 5f)] private float positionStrengthY = 0.5f;
    [Tooltip("Fraction of the camera's rotation offset the item follows.")]
    [SerializeField, Range(0f, 1f)] private float rotationFollow = 0.5f;
    [Tooltip("Lag behind the camera, seconds. 0 = glued to it.")]
    [SerializeField, Min(0f)] private float followSmoothTime = 0.08f;

    [Header("Look sway")]
    [Tooltip("Degrees of sway per degree-per-second of head turn. Positive = the item trails behind the turn.")]
    [SerializeField, Min(0f)] private float swayPerDegreePerSecond = 0.01f;
    [Tooltip("Cap on the look sway, degrees, before strength is applied.")]
    [SerializeField, Min(0f)] private float maxSwayDegrees = 4f;
    [Tooltip("How quickly the sway catches up with a turn and settles back after it.")]
    [SerializeField, Min(0f)] private float swaySmoothTime = 0.12f;

    private Vector3 followPosition;
    private Vector3 followPositionVelocity;
    private Vector3 followRotation;
    private Vector3 followRotationVelocity;
    private Vector2 sway;
    private Vector2 swayVelocity;

    private void Awake()
    {
        if (!ValidateReferences())
            enabled = false;
    }

    private void LateUpdate()
    {
        float deltaTime = Time.deltaTime;

        Vector3 targetPosition = Vector3.zero;
        Vector3 targetRotation = Vector3.zero;
        Vector2 targetSway = Vector2.zero;

        // A disabled effects component (remote player, missing references) has nothing to
        // say; everything eases back to rest instead of freezing mid-bob.
        if (cameraEffects.isActiveAndEnabled)
        {
            CameraEffectOutput output = cameraEffects.LastOutput;
            targetPosition = new Vector3(
                output.PositionOffset.x * positionStrengthX,
                output.PositionOffset.y * positionStrengthY,
                output.PositionOffset.z);
            targetRotation = output.RotationOffset * rotationFollow;

            // Same sign as the turn: the overlay camera yaws with the head, so the item
            // slides the other way on screen - the hand lagging behind the look.
            targetSway = new Vector2(cameraEffects.LastPitchRate, cameraEffects.LastYawRate) * swayPerDegreePerSecond;
            targetSway = Vector2.ClampMagnitude(targetSway, maxSwayDegrees);
        }

        followPosition = Vector3.SmoothDamp(followPosition, targetPosition, ref followPositionVelocity, followSmoothTime, Mathf.Infinity, deltaTime);
        followRotation = Vector3.SmoothDamp(followRotation, targetRotation, ref followRotationVelocity, followSmoothTime, Mathf.Infinity, deltaTime);
        sway = Vector2.SmoothDamp(sway, targetSway, ref swayVelocity, swaySmoothTime, Mathf.Infinity, deltaTime);

        Vector3 rotation = followRotation;
        rotation.x += sway.x;
        rotation.y += sway.y;

        viewmodelCamera.localPosition = followPosition * strength;
        viewmodelCamera.localRotation = Quaternion.Euler(rotation * strength);
    }

    private bool ValidateReferences()
    {
        if (cameraEffects == null)
        {
            Debug.LogError($"{nameof(ViewmodelSway)} requires assigned {nameof(cameraEffects)}.", this);
            return false;
        }

        if (viewmodelCamera == null)
        {
            Debug.LogError($"{nameof(ViewmodelSway)} requires assigned {nameof(viewmodelCamera)}.", this);
            return false;
        }

        return true;
    }
}
