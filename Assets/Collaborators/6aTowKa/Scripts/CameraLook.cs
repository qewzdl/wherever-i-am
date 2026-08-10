using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraLook : MonoBehaviour, ILocalPlayerCameraService
{
    [Header("References")]
    [SerializeField] private Transform playerTransform;

    [Header("Sensitivity")]
    [SerializeField] private float sensitivity = 100f;
    [SerializeField] private float horizontalSensitivityMultiplier = 1f;
    [SerializeField] private float verticalSensitivityMultiplier = 1f;

    [Header("Pitch Clamp")]
    [SerializeField] private float minPitch = -90f;
    [SerializeField] private float maxPitch = 90f;

    [Header("Smoothing")]
    [SerializeField] private float smoothingTime = 0.03f;

    [Header("Cursor")]
    [SerializeField] private bool lockCursorOnLocalControl = true;
    [SerializeField] private bool unlockCursorOnDisable = true;
    [SerializeField] private bool unlockCursorWhenLookBlocked = true;
    [SerializeField] private bool lockCursorWhenLookUnblocked = true;
    [SerializeField] private bool requireLockedCursorToLook = true;

    private readonly HashSet<object> lookBlockers = new();

    private Rigidbody playerRigidbody;
    private IPauseService pauseService;

    private Vector2 pendingLookDelta;

    private float targetPitch;
    private float targetYaw;

    private float currentPitch;
    private float currentYaw;

    private float pitchVelocity;
    private float yawVelocity;

    private bool hasLocalControl;
    private bool lookActive = true;
    private float verticalSensitivitySign = 1f;
    private int appliedSettingsRevision = -1;

    private Transform hidingViewAnchor;
    private Vector3 returnLocalPosition;
    private Quaternion returnLocalRotation;
    private float hidingMinimumYaw;
    private float hidingMaximumYaw;
    private float hidingMinimumPitch;
    private float hidingMaximumPitch;
    private bool hidingAllowsPeeking;
    private bool hasHidingView;

    public bool IsHidingViewActive => hasHidingView;

    private void Awake()
    {
        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        SyncRotationFromScene();

        ApplySettingsIfChanged();
    }

    private void OnEnable()
    {
        if (SettingsService.TryGet(out ISettingsService settings))
            settings.FovChanged += OnFovChanged;
    }

    private void OnFovChanged(float fieldOfView)
    {
        Camera attachedCamera = GetComponentInChildren<Camera>(true);
        if (attachedCamera != null)
            attachedCamera.fieldOfView = Mathf.Clamp(fieldOfView, 50f, 110f);
    }

    public void ApplyUserSettings(
        float mouseSensitivity,
        bool invertVerticalLook,
        bool smoothingEnabled,
        float smoothingIntensity,
        float fieldOfView)
    {
        sensitivity = Mathf.Clamp(mouseSensitivity, GameSettingsData.MinMouseSensitivity, GameSettingsData.MaxMouseSensitivity);
        verticalSensitivitySign = invertVerticalLook ? -1f : 1f;
        smoothingTime = smoothingEnabled
            ? Mathf.Lerp(0.005f, 0.12f, Mathf.Clamp01(smoothingIntensity))
            : 0f;

        Camera attachedCamera = GetComponentInChildren<Camera>(true);

        if (attachedCamera != null)
        {
            attachedCamera.fieldOfView = Mathf.Clamp(fieldOfView, 50f, 110f);
        }
    }

    public void Construct(IPauseService pauseService)
    {
        this.pauseService = pauseService;
    }

    public void SetLocalControl(bool value)
    {
        bool hadLocalControl = hasLocalControl;

        hasLocalControl = value;
        lookBlockers.Clear();
        lookActive = true;
        pendingLookDelta = Vector2.zero;

        if (!hasLocalControl)
        {
            // The cursor is one global thing shared by every camera in the
            // scene, and someone else's player has no business releasing it.
            // Another player joining runs this on their copy here, which
            // unlocked the local player's cursor - and looking around needs it
            // locked, so the camera died until the pause menu locked it again.
            if (hadLocalControl)
            {
                SetCursorLocked(false);
            }

            enabled = false;
            return;
        }

        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        enabled = true;
        SyncRotationFromScene();

        if (lockCursorOnLocalControl)
            SetCursorLocked(true);
    }

    public void SetLookActive(bool value)
    {
        lookBlockers.Clear();
        ApplyLookActive(value);
    }

    public void SetLookActive(object source, bool value)
    {
        if (source == null)
        {
            SetLookActive(value);
            return;
        }

        if (value)
            lookBlockers.Remove(source);
        else
            lookBlockers.Add(source);

        ApplyLookActive(lookBlockers.Count == 0);
    }

    public void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    public bool TrySetHidingView(
        Transform cameraAnchor,
        float minimumYaw,
        float maximumYaw,
        float minimumPitch,
        float maximumPitch,
        bool allowPeeking
    )
    {
        if (!hasLocalControl || cameraAnchor == null)
        {
            return false;
        }

        bool isNewHidingView =
            !hasHidingView ||
            hidingViewAnchor != cameraAnchor;

        if (isNewHidingView)
        {
            returnLocalPosition = transform.localPosition;
            returnLocalRotation = transform.localRotation;
        }

        hidingViewAnchor = cameraAnchor;
        hasHidingView = true;
        hidingMinimumYaw = Mathf.Min(0f, minimumYaw);
        hidingMaximumYaw = Mathf.Max(0f, maximumYaw);
        hidingMinimumPitch = Mathf.Min(0f, minimumPitch);
        hidingMaximumPitch = Mathf.Max(0f, maximumPitch);
        hidingAllowsPeeking = allowPeeking;

        if (!isNewHidingView)
        {
            return true;
        }

        targetYaw = cameraAnchor.eulerAngles.y;
        currentYaw = targetYaw;
        targetPitch = 0f;
        currentPitch = 0f;
        pitchVelocity = 0f;
        yawVelocity = 0f;
        pendingLookDelta = Vector2.zero;

        ApplyCameraRotation();
        return true;
    }

    public void ClearHidingView()
    {
        if (!hasHidingView)
        {
            return;
        }

        hasHidingView = false;
        hidingViewAnchor = null;
        hidingAllowsPeeking = false;
        pendingLookDelta = Vector2.zero;

        transform.localPosition = returnLocalPosition;
        transform.localRotation = returnLocalRotation;
        SyncRotationFromScene();
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        if (!hasLocalControl || !lookActive)
        {
            pendingLookDelta = Vector2.zero;
            return;
        }

        if (context.canceled)
            return;

        pendingLookDelta += context.ReadValue<Vector2>();
    }

    private void Update()
    {
        ApplySettingsIfChanged();

        if (CanReadLookInput())
            ApplyLookInput();

        pendingLookDelta = Vector2.zero;
    }

    private void LateUpdate()
    {
        if (!hasLocalControl)
            return;

        UpdateSmoothedRotation();
        ApplyCameraRotation();
    }

    private void FixedUpdate()
    {
        if (!hasLocalControl ||
            playerRigidbody == null ||
            hasHidingView)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.Euler(0f, currentYaw, 0f);
        playerRigidbody.MoveRotation(targetRotation);
    }

    private void OnDisable()
    {
        if (SettingsService.TryGet(out ISettingsService settings))
            settings.FovChanged -= OnFovChanged;

        pendingLookDelta = Vector2.zero;
        ClearHidingView();

        if (hasLocalControl && unlockCursorOnDisable)
            SetCursorLocked(false);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            return;

        if (hasLocalControl && lookActive && lockCursorOnLocalControl)
            SetCursorLocked(true);
    }

    private bool ValidateReferences()
    {
        if (playerTransform == null)
        {
            Debug.LogError($"{nameof(CameraLook)} requires assigned {nameof(playerTransform)}.", this);
            return false;
        }

        playerRigidbody = playerTransform.GetComponent<Rigidbody>();

        if (playerRigidbody == null)
        {
            Debug.LogError($"{nameof(CameraLook)} requires Rigidbody on assigned player transform.", this);
            return false;
        }

        return true;
    }

    private void SyncRotationFromScene()
    {
        targetPitch = Mathf.Clamp(Mathf.DeltaAngle(0f, transform.localEulerAngles.x), minPitch, maxPitch);
        targetYaw = Mathf.DeltaAngle(0f, playerTransform.eulerAngles.y);

        currentPitch = targetPitch;
        currentYaw = targetYaw;

        pitchVelocity = 0f;
        yawVelocity = 0f;

        ApplyCameraRotation();
    }

    private void ApplyLookActive(bool value)
    {
        if (lookActive == value)
            return;

        lookActive = value;
        pendingLookDelta = Vector2.zero;

        if (!hasLocalControl)
            return;

        if (!lookActive && unlockCursorWhenLookBlocked)
            SetCursorLocked(false);
        else if (lookActive && lockCursorWhenLookUnblocked)
            SetCursorLocked(true);
    }

    private bool CanReadLookInput()
    {
        if (!hasLocalControl)
            return false;

        if (!lookActive)
            return false;

        if (pauseService != null && pauseService.IsPaused)
            return false;
            

        if (requireLockedCursorToLook && Cursor.lockState != CursorLockMode.Locked)
            return false;

        return true;
    }

    private void ApplyLookInput()
    {
        if (pendingLookDelta.sqrMagnitude <= Mathf.Epsilon ||
            (hasHidingView && !hidingAllowsPeeking))
        {
            return;
        }

        if (SettingsService.TryGet(out ISettingsService settings))
            sensitivity = Mathf.Clamp(settings.Current.mouseSensitivity, GameSettingsData.MinMouseSensitivity, GameSettingsData.MaxMouseSensitivity);

        Vector2 scaledDelta = pendingLookDelta * sensitivity / 500f;

        targetYaw += scaledDelta.x * horizontalSensitivityMultiplier;
        targetPitch -= scaledDelta.y * verticalSensitivityMultiplier * verticalSensitivitySign;

        if (hasHidingView && hidingViewAnchor != null)
        {
            float anchorYaw = hidingViewAnchor.eulerAngles.y;
            float relativeYaw = Mathf.DeltaAngle(anchorYaw, targetYaw);
            relativeYaw = Mathf.Clamp(
                relativeYaw,
                hidingMinimumYaw,
                hidingMaximumYaw
            );
            targetYaw = anchorYaw + relativeYaw;
            targetPitch = Mathf.Clamp(
                targetPitch,
                hidingMinimumPitch,
                hidingMaximumPitch
            );
            return;
        }

        targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);
    }

    private void UpdateSmoothedRotation()
    {
        if (smoothingTime <= 0f)
        {
            currentPitch = targetPitch;
            currentYaw = targetYaw;
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;

        currentPitch = Mathf.SmoothDampAngle(currentPitch, targetPitch, ref pitchVelocity, smoothingTime, Mathf.Infinity, deltaTime);
        currentYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref yawVelocity, smoothingTime, Mathf.Infinity, deltaTime);
    }

    private void ApplyCameraRotation()
    {
        if (hasHidingView && hidingViewAnchor != null)
        {
            float anchorYaw = hidingViewAnchor.eulerAngles.y;
            float relativeYaw = Mathf.Clamp(
                Mathf.DeltaAngle(anchorYaw, currentYaw),
                hidingMinimumYaw,
                hidingMaximumYaw
            );
            float constrainedPitch = Mathf.Clamp(
                currentPitch,
                hidingMinimumPitch,
                hidingMaximumPitch
            );

            transform.SetPositionAndRotation(
                hidingViewAnchor.position,
                hidingViewAnchor.rotation *
                Quaternion.Euler(constrainedPitch, relativeYaw, 0f)
            );
            return;
        }

        float yawOffset = Mathf.DeltaAngle(playerTransform.eulerAngles.y, currentYaw);
        transform.localRotation = Quaternion.Euler(currentPitch, yawOffset, 0f);
    }

    private void ApplySettingsIfChanged()
    {
        if (!SettingsService.TryGet(out ISettingsService settings) ||
            settings.Revision == appliedSettingsRevision)
        {
            return;
        }

        GameSettingsData values = settings.Current;
        ApplyUserSettings(
            values.mouseSensitivity,
            values.invertVerticalLook,
            values.cameraSmoothing,
            values.cameraSmoothingIntensity,
            values.fieldOfView);
        appliedSettingsRevision = settings.Revision;
    }
}
