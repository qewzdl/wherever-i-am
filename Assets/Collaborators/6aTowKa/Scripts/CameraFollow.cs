using UnityEngine;

public class CameraFollow : MonoBehaviour, ISettingsServiceConsumer
{
    [Header("References")]
    [SerializeField] private Transform target;

    [Header("Local Offsets")]
    [SerializeField] private Vector3 standingLocalOffset = new Vector3(0f, 0.491f, 0f);
    [SerializeField] private Vector3 crouchingLocalOffset = new Vector3(0f, 0.2455f, 0f);

    [Header("Smoothing")]
    [SerializeField] private float positionSmoothTime = 0.06f;

    private Vector3 currentLocalOffset;
    private Vector3 targetLocalOffset;
    private Vector3 offsetVelocity;

    private bool hasLocalControl;
    private ISettingsService settingsService;

    public void ApplyUserSmoothing(bool smoothingEnabled, float smoothingIntensity)
    {
        positionSmoothTime = smoothingEnabled
            ? Mathf.Lerp(0.01f, 0.2f, Mathf.Clamp01(smoothingIntensity))
            : 0f;

        offsetVelocity = Vector3.zero;
    }

    private void Awake()
    {
        currentLocalOffset = standingLocalOffset;
        targetLocalOffset = standingLocalOffset;

        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        SnapToTarget();
    }

    public void Construct(ISettingsService settings)
    {
        if (settings == null)
            throw new System.ArgumentNullException(nameof(settings));

        if (ReferenceEquals(settingsService, settings))
            return;

        ReleaseSettingsService();
        settingsService = settings;
        settingsService.SettingsChanged += ApplySettings;
        ApplySettings();
    }

    public void ReleaseSettingsService()
    {
        if (settingsService == null)
            return;

        settingsService.SettingsChanged -= ApplySettings;
        settingsService = null;
    }

    private void OnDestroy()
    {
        ReleaseSettingsService();
    }

    public void SetLocalControl(bool value)
    {
        hasLocalControl = value;
        offsetVelocity = Vector3.zero;

        if (!hasLocalControl)
        {
            enabled = false;
            return;
        }

        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        enabled = true;
        SnapToTarget();
    }

    public void SetCrouching(bool isCrouching)
    {
        targetLocalOffset = isCrouching ? crouchingLocalOffset : standingLocalOffset;

        if (!hasLocalControl)
            currentLocalOffset = targetLocalOffset;
    }

    public void SetTarget(Transform value)
    {
        target = value;

        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        SnapToTarget();
    }

    private void LateUpdate()
    {
        if (!hasLocalControl)
            return;

        UpdateOffset();
        transform.position = GetWorldPosition();
    }

    private void UpdateOffset()
    {
        if (positionSmoothTime <= 0f)
        {
            currentLocalOffset = targetLocalOffset;
            return;
        }

        currentLocalOffset = Vector3.SmoothDamp(
            currentLocalOffset,
            targetLocalOffset,
            ref offsetVelocity,
            positionSmoothTime
        );
    }

    private void SnapToTarget()
    {
        currentLocalOffset = targetLocalOffset;
        offsetVelocity = Vector3.zero;
        transform.position = GetWorldPosition();
    }

    private Vector3 GetWorldPosition()
    {
        return target.TransformPoint(currentLocalOffset);
    }

    private bool ValidateReferences()
    {
        if (target == null)
        {
            Debug.LogError($"{nameof(CameraFollow)} requires assigned root target transform.", this);
            return false;
        }

        return true;
    }

    private void ApplySettings()
    {
        if (settingsService == null)
            return;

        GameSettingsData values = settingsService.Current;
        ApplyUserSmoothing(values.cameraSmoothing, values.cameraSmoothingIntensity);
    }
}
