using System;
using UnityEngine;

public class PlayerPostureController : PlayerComponent, IPlayerSignalListener
{
    private const int StandingClearanceBufferSize = 32;

    private readonly Collider[] standingClearanceHits = new Collider[StandingClearanceBufferSize];

    [Serializable]
    private struct PostureDimensions
    {
        [SerializeField, Min(0.01f)] private float colliderHeight;
        [SerializeField] private float cameraHeight;

        public float ColliderHeight => colliderHeight;
        public float CameraHeight => cameraHeight;

        public PostureDimensions(float colliderHeight, float cameraHeight)
        {
            this.colliderHeight = colliderHeight;
            this.cameraHeight = cameraHeight;
        }

        public void Clamp(float minimumColliderHeight)
        {
            colliderHeight = Mathf.Max(minimumColliderHeight, colliderHeight);
        }
    }

    [Header("References")]
    [SerializeField] private CapsuleCollider bodyCollider;
    [SerializeField] private Transform cameraPivot;

    [Header("Posture Dimensions")]
    [SerializeField] private PostureDimensions standing = new(2f, 0.75f);
    [SerializeField] private PostureDimensions crouching = new(1f, 0.4f);

    [Header("Local Camera Transition")]
    [SerializeField, Min(0f)] private float cameraHeightSmoothTime = 0.15f;

    [Header("Standing Clearance")]
    [SerializeField] private LayerMask standingClearanceMask = ~0;
    [SerializeField, Min(0f)] private float standingClearanceSkin = 0.02f;
    [SerializeField] private QueryTriggerInteraction standingClearanceTriggerInteraction = QueryTriggerInteraction.Ignore;

    private Vector3 standingColliderCenter;
    private Vector3 cameraBaseLocalPosition;
    private float targetCameraHeight;
    private float cameraHeightVelocity;

    private bool hasLocalControl;
    private bool isInitialized;
    private bool missingColliderLogged;
    private bool listensToCrouchUpdate;
    private bool listensToCrouchSync;

    protected override void OnPostInit(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner)
    {
        hasLocalControl = !isMultiplayer || isOwner;

        if (!InitializeGeometry())
        {
            enabled = false;
            return;
        }

        signals.CrouchUpdateSignal.Listen(SetCrouching);
        listensToCrouchUpdate = true;

        if (isMultiplayer)
        {
            signals.CrouchSyncSignal.Listen(SetCrouching);
            listensToCrouchSync = true;
        }

        ApplyPosture(false, true);
    }

    public void Cleanup()
    {
        if (listensToCrouchUpdate)
            signals.CrouchUpdateSignal.Unlisten(SetCrouching);

        if (listensToCrouchSync)
            signals.CrouchSyncSignal.Unlisten(SetCrouching);

        listensToCrouchUpdate = false;
        listensToCrouchSync = false;
    }

    public void SetBodyCollider(CapsuleCollider collider)
    {
        bodyCollider = collider;
    }

    public void SetCameraPivot(Transform pivot)
    {
        cameraPivot = pivot;
    }

    public void SetCrouching(bool value)
    {
        if (!isInitialized)
            return;

        ApplyPosture(value, false);
    }

    public bool HasStandingClearance()
    {
        if (!isInitialized || bodyCollider == null)
        {
            LogMissingCollider();
            return false;
        }

        float standingHeight = GetValidColliderHeight(standing.ColliderHeight);
        Vector3 standingCenter = GetColliderCenter(standingHeight);
        Vector3 worldCenter = bodyCollider.transform.TransformPoint(standingCenter);
        Vector3 capsuleAxis = bodyCollider.transform.TransformDirection(GetCapsuleAxis(bodyCollider.direction));

        if (capsuleAxis.sqrMagnitude <= 0.001f)
            capsuleAxis = Vector3.up;

        capsuleAxis.Normalize();

        float worldRadius = Mathf.Max(0.01f, GetScaledCapsuleRadius(bodyCollider));
        float worldHeight = Mathf.Max(
            worldRadius * 2f,
            standingHeight * GetCapsuleHeightScale(bodyCollider)
        );

        float skin = Mathf.Max(0f, standingClearanceSkin);

        worldRadius = Mathf.Max(0.01f, worldRadius - skin);
        worldHeight = Mathf.Max(worldRadius * 2f, worldHeight - skin * 2f);

        float halfSegmentLength = Mathf.Max(0f, worldHeight * 0.5f - worldRadius);
        Vector3 pointA = worldCenter + capsuleAxis * halfSegmentLength;
        Vector3 pointB = worldCenter - capsuleAxis * halfSegmentLength;

        int hitCount = Physics.OverlapCapsuleNonAlloc(
            pointA,
            pointB,
            worldRadius,
            standingClearanceHits,
            standingClearanceMask,
            standingClearanceTriggerInteraction
        );

        bool bufferWasFull = hitCount >= standingClearanceHits.Length;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = standingClearanceHits[i];

            if (hit == null)
                continue;

            if (IsSelfCollider(hit))
                continue;

            return false;
        }

        return !bufferWasFull;
    }

    private void LateUpdate()
    {
        if (!hasLocalControl || cameraPivot == null)
            return;

        float nextHeight = cameraHeightSmoothTime <= 0f
            ? targetCameraHeight
            : Mathf.SmoothDamp(
                cameraPivot.localPosition.y,
                targetCameraHeight,
                ref cameraHeightVelocity,
                cameraHeightSmoothTime
            );

        cameraPivot.localPosition = new Vector3(
            cameraBaseLocalPosition.x,
            nextHeight,
            cameraBaseLocalPosition.z
        );
    }

    private bool InitializeGeometry()
    {
        if (bodyCollider == null)
        {
            LogMissingCollider();
            return false;
        }

        if (cameraPivot == null && hasLocalControl)
        {
            Debug.LogError($"{nameof(PlayerPostureController)} requires assigned camera pivot for local control.", this);
            return false;
        }

        standingColliderCenter = bodyCollider.center;

        if (cameraPivot != null)
            cameraBaseLocalPosition = cameraPivot.localPosition;

        isInitialized = true;
        return true;
    }

    private void ApplyPosture(bool isCrouching, bool snapCamera)
    {
        PostureDimensions dimensions = isCrouching ? crouching : standing;
        float colliderHeight = GetValidColliderHeight(dimensions.ColliderHeight);

        bodyCollider.height = colliderHeight;
        bodyCollider.center = GetColliderCenter(colliderHeight);

        targetCameraHeight = dimensions.CameraHeight;

        if (hasLocalControl && cameraPivot != null && snapCamera)
        {
            cameraHeightVelocity = 0f;
            cameraPivot.localPosition = new Vector3(
                cameraBaseLocalPosition.x,
                targetCameraHeight,
                cameraBaseLocalPosition.z
            );
        }
    }

    private Vector3 GetColliderCenter(float height)
    {
        Vector3 axis = GetCapsuleAxis(bodyCollider.direction);
        float standingHeight = GetValidColliderHeight(standing.ColliderHeight);
        Vector3 standingBottom = standingColliderCenter - axis * (standingHeight * 0.5f);

        return standingBottom + axis * (height * 0.5f);
    }

    private float GetValidColliderHeight(float configuredHeight)
    {
        return Mathf.Max(bodyCollider.radius * 2f, configuredHeight);
    }

    private void LogMissingCollider()
    {
        if (missingColliderLogged)
            return;

        Debug.LogError($"{nameof(PlayerPostureController)} requires assigned {nameof(CapsuleCollider)}.", this);
        missingColliderLogged = true;
    }

    private bool IsSelfCollider(Collider hit)
    {
        Transform hitTransform = hit.transform;

        return hitTransform == transform || hitTransform.IsChildOf(transform);
    }

    private static Vector3 GetCapsuleAxis(int capsuleDirection)
    {
        return capsuleDirection switch
        {
            0 => Vector3.right,
            2 => Vector3.forward,
            _ => Vector3.up
        };
    }

    private static float GetCapsuleHeightScale(CapsuleCollider capsuleCollider)
    {
        Vector3 scale = Abs(capsuleCollider.transform.lossyScale);

        return capsuleCollider.direction switch
        {
            0 => scale.x,
            2 => scale.z,
            _ => scale.y
        };
    }

    private static float GetScaledCapsuleRadius(CapsuleCollider capsuleCollider)
    {
        Vector3 scale = Abs(capsuleCollider.transform.lossyScale);

        return capsuleCollider.direction switch
        {
            0 => capsuleCollider.radius * Mathf.Max(scale.y, scale.z),
            2 => capsuleCollider.radius * Mathf.Max(scale.x, scale.y),
            _ => capsuleCollider.radius * Mathf.Max(scale.x, scale.z)
        };
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(
            Mathf.Abs(value.x),
            Mathf.Abs(value.y),
            Mathf.Abs(value.z)
        );
    }

    private void OnValidate()
    {
        float minimumHeight = bodyCollider != null
            ? Mathf.Max(0.01f, bodyCollider.radius * 2f)
            : 0.01f;

        standing.Clamp(minimumHeight);
        crouching.Clamp(minimumHeight);
        cameraHeightSmoothTime = Mathf.Max(0f, cameraHeightSmoothTime);
        standingClearanceSkin = Mathf.Max(0f, standingClearanceSkin);

        if (Application.isPlaying)
            return;

        if (bodyCollider != null)
        {
            Vector3 axis = GetCapsuleAxis(bodyCollider.direction);
            Vector3 bottom = bodyCollider.center - axis * (bodyCollider.height * 0.5f);

            bodyCollider.height = standing.ColliderHeight;
            bodyCollider.center = bottom + axis * (standing.ColliderHeight * 0.5f);
        }

        if (cameraPivot != null)
        {
            Vector3 localPosition = cameraPivot.localPosition;
            localPosition.y = standing.CameraHeight;
            cameraPivot.localPosition = localPosition;
        }
    }
}
