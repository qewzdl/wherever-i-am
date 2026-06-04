using UnityEngine;

public class PlayerAnimation : PlayerComponent, IPlayerSignalListener
{
    private const int StandingClearanceBufferSize = 32;

    private readonly Collider[] standingClearanceHits = new Collider[StandingClearanceBufferSize];

    [Header("Physical Crouch")]
    [SerializeField] private CapsuleCollider bodyCollider;
    [SerializeField] private float crouchColliderHeight = 1f;
    [SerializeField] private float standColliderHeight = 2f;
    [SerializeField] private Vector3 crouchColliderCenter = new Vector3(0f, -0.5f, 0f);
    [SerializeField] private Vector3 standColliderCenter = Vector3.zero;

    [Header("Standing Clearance")]
    [SerializeField] private LayerMask standingClearanceMask = ~0;
    [SerializeField, Min(0f)] private float standingClearanceSkin = 0.02f;
    [SerializeField] private QueryTriggerInteraction standingClearanceTriggerInteraction = QueryTriggerInteraction.Ignore;

    private bool missingColliderLogged;
    private bool listensToCrouchUpdate;
    private bool listensToCrouchSync;

    protected override void OnPostInit(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner)
    {
        signals.CrouchUpdateSignal.Listen(SetupAnimation);
        listensToCrouchUpdate = true;

        if (isMultiplayer)
        {
            signals.CrouchSyncSignal.Listen(SetupAnimation);
            listensToCrouchSync = true;
        }

        ApplyColliderState(false);
    }

    public void Cleanup()
    {
        if (listensToCrouchUpdate)
            signals.CrouchUpdateSignal.Unlisten(SetupAnimation);

        if (listensToCrouchSync)
            signals.CrouchSyncSignal.Unlisten(SetupAnimation);

        listensToCrouchUpdate = false;
        listensToCrouchSync = false;
    }

    public void SetBodyCollider(CapsuleCollider collider)
    {
        bodyCollider = collider;
    }

    public void SetupAnimation(bool isCrouching)
    {
        ApplyColliderState(isCrouching);
    }

    public bool HasStandingClearance()
    {
        if (bodyCollider == null)
        {
            LogMissingCollider();
            return false;
        }

        Vector3 worldCenter = bodyCollider.transform.TransformPoint(standColliderCenter);
        Vector3 capsuleAxis = bodyCollider.transform.TransformDirection(GetCapsuleAxis(bodyCollider.direction));

        if (capsuleAxis.sqrMagnitude <= 0.001f)
            capsuleAxis = Vector3.up;

        capsuleAxis.Normalize();

        float worldRadius = Mathf.Max(0.01f, GetScaledCapsuleRadius(bodyCollider));
        float worldHeight = Mathf.Max(
            worldRadius * 2f,
            standColliderHeight * GetCapsuleHeightScale(bodyCollider)
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

    private void ApplyColliderState(bool isCrouching)
    {
        if (bodyCollider == null)
        {
            LogMissingCollider();
            return;
        }

        bodyCollider.height = Mathf.Max(0.01f, isCrouching ? crouchColliderHeight : standColliderHeight);
        bodyCollider.center = isCrouching ? crouchColliderCenter : standColliderCenter;
    }

    private void LogMissingCollider()
    {
        if (missingColliderLogged)
            return;

        Debug.LogError($"{nameof(PlayerAnimation)} requires assigned {nameof(CapsuleCollider)} for physical crouch.", this);
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
}
