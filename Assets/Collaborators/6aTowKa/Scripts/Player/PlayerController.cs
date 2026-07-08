using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : PlayerComponent, IPlayerSignalListener
{
    [Header("Movement")]
    [SerializeField, Min(0f)] private float speed = 5f;
    [SerializeField, Range(0f, 1f)] private float crouchSpeedMultiplier = 0.55f;
    [SerializeField, Min(0f)] private float acceleration = 30f;
    [SerializeField, Min(0f)] private float deceleration = 40f;
    [SerializeField, Range(0f, 1f)] private float airControlMultiplier = 0.35f;
    [SerializeField, Min(0f)] private float moveInputDeadZone = 0.1f;

    [Header("References")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private CapsuleCollider bodyCollider;
    [SerializeField] private PlayerPostureController playerPosture;

    [Header("Collision")]
    [SerializeField, Range(0f, 1f)] private float blockingContactMaxY = 0.7f;
    [SerializeField, Min(0f)] private float itemPushSpeed = 1.5f;
    [SerializeField, Min(0f)] private float itemPushMassInfluence = 0.3f;

    [Header("Gravity Settings")]
    [SerializeField, Min(1f)] private float gravityMultiplier = 1f;

    [Header("Ground Check")]
    [SerializeField] private LayerMask groundLayer = ~0;
    [SerializeField, Min(0f)] private float groundCheckDistance = 0.1f;

    private Vector2 direction;
    private bool isCrouching;
    private bool hasLocalControl;
    private bool listensToCrouchSync;

    private readonly List<Vector3> blockingContactNormals = new(8);
    private readonly List<(Vector3 normal, float allowedIntoSpeed)> itemVelocityConstraints = new(8);
    private readonly RaycastHit[] groundHits = new RaycastHit[8];

    protected override void OnPostInit(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner)
    {
        hasLocalControl = !isMultiplayer || isOwner;

        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;

        if (bodyCollider == null)
            bodyCollider = GetComponentInChildren<CapsuleCollider>();

        if (playerPosture == null)
            playerPosture = GetComponent<PlayerPostureController>();

        signals.MoveSignal.Listen(SetDirection);
        signals.CrouchInputSignal.Listen(UpdateIsCrouching);

        if (isMultiplayer && hasLocalControl)
        {
            signals.CrouchSyncSignal.Listen(SyncCrouchState);
            listensToCrouchSync = true;
        }
    }

    public void Cleanup()
    {
        signals.MoveSignal.Unlisten(SetDirection);
        signals.CrouchInputSignal.Unlisten(UpdateIsCrouching);

        if (listensToCrouchSync)
            signals.CrouchSyncSignal.Unlisten(SyncCrouchState);

        listensToCrouchSync = false;
    }

    public void SetBodyCollider(CapsuleCollider collider)
    {
        bodyCollider = collider;
    }

    public void SetPlayerPosture(PlayerPostureController posture)
    {
        playerPosture = posture;
    }

    private void FixedUpdate()
    {
        bool isGrounded = IsGrounded();

        CacheDraggedItemConstraints();
        Move(isGrounded);
        ApplyExtraGravity(isGrounded);
        blockingContactNormals.Clear();
        itemVelocityConstraints.Clear();
    }

    private void Move(bool isGrounded)
    {
        Vector3 localDirection = new Vector3(direction.x, 0f, direction.y);
        Vector3 worldDirection = rb.rotation * localDirection;

        Vector3 currentHorizontalVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        Vector3 targetHorizontalVelocity = worldDirection * GetTargetSpeed();

        float moveRate = targetHorizontalVelocity.sqrMagnitude > 0f ? acceleration : deceleration;
        if (!isGrounded)
            moveRate *= airControlMultiplier;

        Vector3 horizontalVelocity = Vector3.MoveTowards(
            currentHorizontalVelocity,
            targetHorizontalVelocity,
            moveRate * Time.fixedDeltaTime
        );

        horizontalVelocity = ClipVelocityAgainstBlockingContacts(horizontalVelocity);
        horizontalVelocity = ApplyItemVelocityConstraints(horizontalVelocity);

        rb.linearVelocity = new Vector3(
            horizontalVelocity.x,
            rb.linearVelocity.y,
            horizontalVelocity.z
        );
    }

    private Vector3 ClipVelocityAgainstBlockingContacts(Vector3 velocity)
    {
        for (int i = 0; i < blockingContactNormals.Count; i++)
        {
            Vector3 normal = blockingContactNormals[i];
            float intoSurfaceSpeed = Vector3.Dot(velocity, normal);

            if (intoSurfaceSpeed < 0f)
                velocity -= normal * intoSurfaceSpeed;
        }

        velocity.y = 0f;
        return velocity;
    }

    // How fast the player may press into a given item: heavier items are pushed slower
    // (same falloff shape the drag speed penalty uses).
    private float GetItemPushSpeed(DraggableObject item)
    {
        return itemPushSpeed / (1f + item.Mass * itemPushMassInfluence);
    }

    // Same projection as the wall clip, but against a moving limit: the player may keep moving
    // toward a draggable item as fast as the item itself is yielding (plus a push allowance for
    // resting items). Pushing works and following a lagging carried item doesn't stutter.
    // The constraint only removes velocity, it never adds any (allowedIntoSpeed <= 0).
    private Vector3 ApplyItemVelocityConstraints(Vector3 velocity)
    {
        for (int i = 0; i < itemVelocityConstraints.Count; i++)
        {
            (Vector3 normal, float allowedIntoSpeed) constraint = itemVelocityConstraints[i];
            float intoSpeed = Vector3.Dot(velocity, constraint.normal);

            if (intoSpeed < constraint.allowedIntoSpeed)
                velocity -= constraint.normal * (intoSpeed - constraint.allowedIntoSpeed);
        }

        velocity.y = 0f;
        return velocity;
    }

    private void ApplyExtraGravity(bool isGrounded)
    {
        if (gravityMultiplier <= 1f)
            return;

        if (isGrounded)
            return;

        rb.linearVelocity += Physics.gravity * ((gravityMultiplier - 1f) * Time.fixedDeltaTime);
    }

    private float GetTargetSpeed()
    {
        float targetSpeed = speed;

        if (isCrouching)
            return targetSpeed * crouchSpeedMultiplier;

        return targetSpeed;
    }

    private bool IsGrounded()
    {
        Vector3 origin = transform.position + Vector3.up * 0.05f;
        Vector3 directionToGround = Vector3.down;
        float radius = 0.1f;
        float distance = groundCheckDistance + 0.05f;

        if (bodyCollider != null)
        {
            Transform colliderTransform = bodyCollider.transform;
            Vector3 axis = GetCapsuleAxis(bodyCollider);
            Vector3 worldAxis = colliderTransform.TransformDirection(axis).normalized;
            Vector3 center = colliderTransform.TransformPoint(bodyCollider.center);

            float scaledHeight = GetScaledCapsuleHeight(bodyCollider);
            float scaledRadius = GetScaledCapsuleRadius(bodyCollider) * 0.9f;
            float halfHeight = Mathf.Max(scaledRadius, scaledHeight * 0.5f);

            origin = center - worldAxis * Mathf.Max(0f, halfHeight - scaledRadius);
            origin += worldAxis * groundCheckDistance;
            directionToGround = -worldAxis;
            radius = Mathf.Max(0.01f, scaledRadius);
            distance = groundCheckDistance + 0.02f;
        }

        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            radius,
            directionToGround,
            groundHits,
            distance,
            groundLayer,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = groundHits[i].collider;

            if (hitCollider == null)
                continue;

            if (hitCollider.transform.IsChildOf(transform))
                continue;

            return true;
        }

        return false;
    }

    private static Vector3 GetCapsuleAxis(CapsuleCollider capsuleCollider)
    {
        return capsuleCollider.direction switch
        {
            0 => Vector3.right,
            2 => Vector3.forward,
            _ => Vector3.up
        };
    }

    private static float GetScaledCapsuleHeight(CapsuleCollider capsuleCollider)
    {
        Vector3 scale = Abs(capsuleCollider.transform.lossyScale);

        return capsuleCollider.direction switch
        {
            0 => capsuleCollider.height * scale.x,
            2 => capsuleCollider.height * scale.z,
            _ => capsuleCollider.height * scale.y
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

    private void OnCollisionEnter(Collision collision)
    {
        CacheBlockingContactNormals(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        CacheBlockingContactNormals(collision);
    }

    // Carried DraggableObjects have their physical contact with players disabled, so collision
    // callbacks never fire for them. Detect them via overlap queries instead and constrain the
    // player's velocity: approach no faster than the item actually yields (0 when it's pinned
    // against a wall — brace and slide, no solver push, no stutter while it retreats).
    private void CacheDraggedItemConstraints()
    {
        // Below this measured speed the item counts as pinned rather than yielding.
        const float minItemYieldSpeed = 0.1f;

        if (bodyCollider == null)
            return;

        List<DraggableObject> draggedObjects = DraggableObject.ActiveDraggedObjects;

        if (draggedObjects.Count == 0)
            return;

        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        Vector3 predictedOffset = horizontalVelocity * Time.fixedDeltaTime;
        Transform bodyTransform = bodyCollider.transform;

        for (int i = 0; i < draggedObjects.Count; i++)
        {
            DraggableObject draggedObject = draggedObjects[i];

            if (draggedObject == null)
                continue;

            Collider[] itemColliders = draggedObject.Colliders;

            for (int c = 0; c < itemColliders.Length; c++)
            {
                Collider itemCollider = itemColliders[c];

                if (itemCollider == null || itemCollider.isTrigger)
                    continue;

                // Test the pose the player would reach next tick, so the clip kicks in
                // before the capsule actually enters the item.
                bool overlapped = Physics.ComputePenetration(
                    bodyCollider,
                    bodyTransform.position + predictedOffset,
                    bodyTransform.rotation,
                    itemCollider,
                    itemCollider.transform.position,
                    itemCollider.transform.rotation,
                    out Vector3 direction,
                    out float distance
                );

                if (!overlapped)
                    continue;

                // direction points from the item toward the player — same convention
                // as collision contact normals cached below.
                Vector3 normal = direction;

                if (normal.y > blockingContactMaxY)
                    continue;

                normal.y = 0f;

                if (normal.sqrMagnitude <= Mathf.Epsilon)
                    continue;

                normal = normal.normalized;

                // Item's real speed away from the player along the contact normal.
                float retreatSpeed = -Vector3.Dot(draggedObject.CurrentVelocity, normal);
                float allowedIntoSpeed;

                if (retreatSpeed > minItemYieldSpeed)
                {
                    // The item is yielding: allow pressing into it slightly faster than it moves —
                    // the item's own separation push then carries it along (bulldozing).
                    allowedIntoSpeed = -(retreatSpeed + GetItemPushSpeed(draggedObject));
                }
                else
                {
                    // The item is pinned (e.g. against a wall): hard brace, no sinking in.
                    allowedIntoSpeed = Mathf.Min(-retreatSpeed, 0f);
                }

                itemVelocityConstraints.Add((normal, allowedIntoSpeed));
            }
        }
    }

    private void CacheBlockingContactNormals(Collision collision)
    {
        // Resting draggable items are not hard walls: instead of a full stop, the player gets a
        // velocity constraint with a push allowance, so the body can shove them while still
        // sliding along their surface.
        DraggableObject draggable = collision.collider.GetComponentInParent<DraggableObject>();

        for (int i = 0; i < collision.contactCount; i++)
        {
            Vector3 normal = collision.GetContact(i).normal;

            if (normal.y > blockingContactMaxY)
                continue;

            normal.y = 0f;

            if (normal.sqrMagnitude <= Mathf.Epsilon)
                continue;

            normal = normal.normalized;

            if (draggable != null)
            {
                float allowedIntoSpeed = Vector3.Dot(draggable.CurrentVelocity, normal) - GetItemPushSpeed(draggable);

                if (allowedIntoSpeed > 0f)
                    allowedIntoSpeed = 0f;

                itemVelocityConstraints.Add((normal, allowedIntoSpeed));
            }
            else
            {
                blockingContactNormals.Add(normal);
            }
        }
    }

    public void SetDirection(Vector2 value)
    {
        direction = value.sqrMagnitude < moveInputDeadZone * moveInputDeadZone
            ? Vector2.zero
            : Vector2.ClampMagnitude(value, 1f);
    }

    public void UpdateIsCrouching()
    {
        bool nextIsCrouching = !isCrouching;

        if (!nextIsCrouching && !CanStandUp())
            return;

        SetCrouchState(nextIsCrouching, true);
    }

    private void SyncCrouchState(bool value)
    {
        SetCrouchState(value, false);
    }

    private void SetCrouchState(bool value, bool notify)
    {
        if (isCrouching == value)
            return;

        isCrouching = value;

        if (notify)
            signals.CrouchUpdateSignal.Trigger(isCrouching);
    }

    private bool CanStandUp()
    {
        return playerPosture != null && playerPosture.HasStandingClearance();
    }

    public void SetSpeed(float newSpeed)
    {
        speed = newSpeed;
    }

    public float GetSpeed()
    {
        return speed;
    }
}
