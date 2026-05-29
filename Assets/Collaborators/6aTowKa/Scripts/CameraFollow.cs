using UnityEngine;

public class CameraFollow : MonoBehaviour
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
}