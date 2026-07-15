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

    private void Awake()
    {
        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        SyncRotationFromScene();
    }

    public void Construct(IPauseService pauseService)
    {
        this.pauseService = pauseService;
    }

    public void SetLocalControl(bool value)
    {
        hasLocalControl = value;
        lookBlockers.Clear();
        lookActive = true;
        pendingLookDelta = Vector2.zero;

        if (!hasLocalControl)
        {
            SetCursorLocked(false);
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
        if (!hasLocalControl || playerRigidbody == null)
            return;

        Quaternion targetRotation = Quaternion.Euler(0f, currentYaw, 0f);
        playerRigidbody.MoveRotation(targetRotation);
    }

    private void OnDisable()
    {
        pendingLookDelta = Vector2.zero;

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
        if (pendingLookDelta.sqrMagnitude <= Mathf.Epsilon)
            return;

        Vector2 scaledDelta = pendingLookDelta * sensitivity / 500f;

        targetYaw += scaledDelta.x * horizontalSensitivityMultiplier;
        targetPitch -= scaledDelta.y * verticalSensitivityMultiplier;
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
        float yawOffset = Mathf.DeltaAngle(playerTransform.eulerAngles.y, currentYaw);
        transform.localRotation = Quaternion.Euler(currentPitch, yawOffset, 0f);
    }
}
