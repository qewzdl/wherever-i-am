using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraLook : MonoBehaviour, ILocalPlayerCameraService, ISettingsServiceConsumer
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
    private Camera ownedCamera;
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
    private ISettingsService settingsService;

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
        ownedCamera = GetComponentInChildren<Camera>(true);

        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        SyncRotationFromScene();
    }

    public void Construct(ISettingsService settings)
    {
        if (settings == null)
            throw new System.ArgumentNullException(nameof(settings));

        if (ReferenceEquals(settingsService, settings))
            return;

        ReleaseSettingsService();
        settingsService = settings;
        settingsService.FovChanged += OnFovChanged;
        settingsService.SettingsChanged += ApplySettings;
        ApplySettings();
    }

    public void ReleaseSettingsService()
    {
        if (settingsService == null)
            return;

        settingsService.FovChanged -= OnFovChanged;
        settingsService.SettingsChanged -= ApplySettings;
        settingsService = null;
    }

    private void OnFovChanged(float fieldOfView)
    {
        if (ownedCamera != null)
            ownedCamera.fieldOfView = Mathf.Clamp(fieldOfView, 50f, 110f);
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

        if (ownedCamera != null)
        {
            ownedCamera.fieldOfView = Mathf.Clamp(fieldOfView, 50f, 110f);
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

        // The scene keeps a camera alive for the time before this player
        // exists; claiming the view here is what switches that one off.
        if (hasLocalControl)
            FallbackCamera.SetLocalPlayerCamera(ownedCamera);
        else
            FallbackCamera.ClearLocalPlayerCamera(ownedCamera);

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
        pendingLookDelta = Vector2.zero;
        ClearHidingView();

        if (hasLocalControl && unlockCursorOnDisable)
            SetCursorLocked(false);
    }

    private void OnDestroy()
    {
        ReleaseSettingsService();

        // Leaving the match takes the player's camera with it, so the scene
        // has to get the view back instead of nothing rendering at all.
        FallbackCamera.ClearLocalPlayerCamera(ownedCamera);
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

    private void ApplySettings()
    {
        if (settingsService == null)
            return;

        GameSettingsData values = settingsService.Current;
        ApplyUserSettings(
            values.mouseSensitivity,
            values.invertVerticalLook,
            values.cameraSmoothing,
            values.cameraSmoothingIntensity,
            values.fieldOfView);
    }
}
