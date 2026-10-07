using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : PlayerComponent, IPlayerSignalListener, ISettingsServiceConsumer
{
    [Header("Movement")]
    // The base speed, which is not a constant: DraggableObject scales it while
    // something heavy is being pulled and puts it back afterwards. The gaits
    // are multipliers over whatever it currently is, so the drag penalty
    // composes with them instead of being argued with.
    [SerializeField, Min(0f)] private float speed = 5f;

    // The ladder the gaits are cut from, shared with whatever has to judge how
    // loud a footstep is. Two places keeping their own idea of what running
    // means is a pair of numbers that agree until one of them is balanced.
    [SerializeField] private PlayerMovementProfile movement;
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

    // A body pushed by its velocity stops at the first vertical face it meets,
    // however low: the edge of a rug, a plank on the floor, the bottom stair.
    // Anything up to this high is stepped up onto instead.
    [Header("Steps")]
    [SerializeField, Min(0f)] private float maxStepHeight = 0.35f;

    private Vector2 direction;
    private bool isCrouching;
    private bool isRunning;

    // One over the tank, one for whether the tank has been emptied and not yet
    // recovered enough to be worth opening again.
    private float stamina = 1f;
    private bool isWinded;

    // How much of the tank this spell of exertion has cost, which is not the
    // same as how empty the tank is. Sprinting a third of it away and stopping
    // leaves two thirds in the tank and a third spent, and it is the spent
    // figure that says how hard somebody just worked.
    private float staminaSpentInBurst;
    private bool crouchIsHold;
    private bool wantsToStand;
    private ISettingsService settingsService;
    private bool hasLocalControl;
    private bool listensToCrouchSync;
    private readonly HashSet<object> movementBlockers = new();

    private readonly List<Vector3> blockingContactNormals = new(8);
    private readonly List<(Vector3 normal, float allowedIntoSpeed)> itemVelocityConstraints = new(8);
    private readonly RaycastHit[] groundHits = new RaycastHit[8];
    private readonly RaycastHit[] stepHits = new RaycastHit[8];
    private bool onFooting;
    private Vector3 footingDirection = Vector3.forward;

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
        signals.RunInputSignal.Listen(UpdateIsRunning);

        if (isMultiplayer && hasLocalControl)
        {
            signals.CrouchSyncSignal.Listen(SyncCrouchState);
            listensToCrouchSync = true;
        }
    }

    public void Cleanup()
    {
        if (signals == null)
        {
            listensToCrouchSync = false;
            return;
        }

        signals.MoveSignal.Unlisten(SetDirection);
        signals.CrouchInputSignal.Unlisten(UpdateIsCrouching);
        signals.RunInputSignal.Unlisten(UpdateIsRunning);

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
        bool isGrounded = CheckGrounded();

        // Held up on the edge of a step is standing, as far as walking, the
        // head bob and everything else is concerned.
        IsGrounded = isGrounded || onFooting;

        TickStamina(Time.fixedDeltaTime);
        UpdatePendingStand();
        CacheDraggedItemConstraints();

        Vector3 horizontalVelocity = NextHorizontalVelocity(IsGrounded);
        float toFooting = 0f;
        bool steppedUp = false;
        onFooting = IsGrounded && TryFindFooting(isGrounded, ref horizontalVelocity, out toFooting, out steppedUp);

        // The edge just stepped up is not a wall to stop against.
        if (steppedUp)
            blockingContactNormals.Clear();

        Move(horizontalVelocity);

        if (onFooting)
            MoveUpBy(toFooting, steppedUp);
        else
            ApplyExtraGravity(isGrounded);

        blockingContactNormals.Clear();
        itemVelocityConstraints.Clear();
    }

    private Vector3 NextHorizontalVelocity(bool isGrounded)
    {
        if (movementBlockers.Count > 0)
        {
            direction = Vector2.zero;
        }

        Vector3 localDirection = new Vector3(direction.x, 0f, direction.y);
        Vector3 worldDirection = rb.rotation * localDirection;

        Vector3 currentHorizontalVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        Vector3 targetHorizontalVelocity = worldDirection * GetTargetSpeed();

        float moveRate = targetHorizontalVelocity.sqrMagnitude > 0f ? acceleration : deceleration;
        if (!isGrounded)
            moveRate *= airControlMultiplier;

        return Vector3.MoveTowards(
            currentHorizontalVelocity,
            targetHorizontalVelocity,
            moveRate * Time.fixedDeltaTime
        );
    }

    private void Move(Vector3 horizontalVelocity)
    {
        horizontalVelocity = ClipVelocityAgainstBlockingContacts(horizontalVelocity);
        horizontalVelocity = ApplyItemVelocityConstraints(horizontalVelocity);

        rb.linearVelocity = new Vector3(
            horizontalVelocity.x,
            rb.linearVelocity.y,
            horizontalVelocity.z
        );
    }

    // What the body stands on and steps up onto: whatever it collides with,
    // as far as groundLayer allows. groundLayer alone was the Default layer,
    // and a map's floors and furniture are on Walls, Interactable and the
    // rest - a player on any of them counted as in the air, and nothing on
    // them was ever a step.
    private int SolidLayers
    {
        get
        {
            int layers = 0;

            for (int layer = 0; layer < 32; layer++)
            {
                if (!Physics.GetIgnoreLayerCollision(gameObject.layer, layer))
                    layers |= 1 << layer;
            }

            return groundLayer & layers;
        }
    }

    // How far the body was just put up, onto a step. Heard by the camera,
    // which follows it smoothly rather than in the one move the body makes.
    public event System.Action<float> SteppedUp;

    // The body stands on its footing: the highest flat ground under the
    // middle of it and under the half of it it is moving into - the way a
    // foot would, not the round bottom of the capsule.
    //
    // Ground there higher than it stands, by no more than a step, and with
    // room for the whole body that much higher - nothing above to rise into,
    // nothing in front at that height, into a vent upright it does not go -
    // and the body is put up onto it where it is, the camera following
    // smoothly (StepSmoothing). Moved, not pushed: it was sent up at the
    // speed that got it there in one physics step, and that was a shove. Over the edge, until the round
    // bottom of it rests on the step, it is held up there rather than
    // falling back off.
    //
    // It used to go up by the round of its bottom instead: let down onto
    // the edge where it was about to move, every physics step. Half a metre
    // across, the round met every stair a good way short of it and rose
    // steeply along it, pressed against it and slowed by it - the player
    // slid up the face of every stair.
    //
    // Only flat ground: a slope is walked up, not stepped. Only the world's
    // own: an item or somebody else is not a step.
    private bool TryFindFooting(bool isGrounded, ref Vector3 horizontalVelocity, out float toFooting, out bool steppedUp)
    {
        toFooting = 0f;
        steppedUp = false;

        if (bodyCollider == null ||
            maxStepHeight <= 0f ||
            movementBlockers.Count > 0)
        {
            return false;
        }

        Vector3 move = horizontalVelocity * Time.fixedDeltaTime;
        bool moving = move.sqrMagnitude > 0.000001f;

        if (moving)
            footingDirection = move.normalized;

        Vector3 axis = bodyCollider.transform.TransformDirection(GetCapsuleAxis(bodyCollider)).normalized;
        Vector3 center = bodyCollider.transform.TransformPoint(bodyCollider.center);
        float radius = GetScaledCapsuleRadius(bodyCollider);
        float halfHeight = Mathf.Max(radius, GetScaledCapsuleHeight(bodyCollider) * 0.5f);
        float feet = (center - axis * halfHeight).y;

        if (!TryFootingHeight(center + move, radius + 0.03f, feet, out float footing))
            return false;

        // A hair above it, off the edge.
        const float skin = 0.005f;
        float rise = footing - feet;

        if (rise > 0.002f)
        {
            if (!moving || rise > maxStepHeight)
                return false;

            Vector3 bottom = center - axis * (halfHeight - radius);
            Vector3 top = center + axis * (halfHeight - radius);
            float castRadius = Mathf.Max(0.01f, radius - skin);

            if (TryCapsuleCastAny(bottom, top, castRadius, Vector3.up, rise + skin, out _) ||
                !TryMoveAt(bottom, top, castRadius, rise + skin, ref move))
            {
                return false;
            }

            horizontalVelocity = move / Time.fixedDeltaTime;
            toFooting = rise + skin;
            steppedUp = true;
            return true;
        }

        // Standing on the ground under the round bottom is the physics' own
        // business; held up over an edge it has nothing under it.
        if (isGrounded || rise < -0.02f)
            return false;

        toFooting = rise + skin;
        return true;
    }

    // The highest flat ground the middle of the body and the front half of
    // it stand over, between a step below the feet and a step above them.
    //
    // Looked for a little past the edge of the body, and every eighth of a
    // right angle round its front: a step is found before the body meets
    // it, whichever way the body comes at it. Every half right angle and
    // just inside the edge, a step met at a slant fell between two looks -
    // the body ran into it first, was turned along it, and got up only
    // walking straight at it.
    private bool TryFootingHeight(Vector3 center, float reach, float feet, out float footing)
    {
        footing = float.NegativeInfinity;
        float from = feet + maxStepHeight + 0.05f;

        for (int i = 0; i < 10; i++)
        {
            Vector3 at = center;

            if (i > 0)
                at += Quaternion.AngleAxis(-90f + (i - 1) * 22.5f, Vector3.up) * footingDirection * reach;

            at.y = from;

            if (TryRaycastStatic(at, maxStepHeight + 0.1f, out RaycastHit ground) &&
                ground.normal.y > 0.95f &&
                ground.point.y > footing)
            {
                footing = ground.point.y;
            }
        }

        return !float.IsNegativeInfinity(footing);
    }

    // Whether the body, this high up, can make the move - straight, or
    // sliding along whatever stands in the way, as walking does.
    private bool TryMoveAt(Vector3 bottom, Vector3 top, float castRadius, float raise, ref Vector3 move)
    {
        Vector3 raised = Vector3.up * raise;

        if (!TryCapsuleCastAny(bottom + raised, top + raised, castRadius, move.normalized, move.magnitude, out RaycastHit wall))
            return true;

        Vector3 normal = wall.normal;
        normal.y = 0f;

        if (wall.distance <= 0f || normal.sqrMagnitude < 0.0001f)
            return false;

        normal.Normalize();
        Vector3 along = move - normal * Mathf.Min(0f, Vector3.Dot(move, normal));

        if (along.sqrMagnitude < 0.000001f ||
            TryCapsuleCastAny(bottom + raised, top + raised, castRadius, along.normalized, along.magnitude, out _))
        {
            return false;
        }

        move = along;
        return true;
    }

    // Put up onto the footing where it is, not pushed there: the body is
    // moved, and nothing carries it on up afterwards. Its fall is taken off
    // so that it stays at that height until it stands there - whatever
    // gravity would add this physics step, and no more.
    private void MoveUpBy(float distance, bool steppedUp)
    {
        BodyPlacement.Place(transform, rb, rb.position + Vector3.up * distance, rb.rotation, stop: false);

        Vector3 velocity = rb.linearVelocity;
        velocity.y = rb.useGravity ? -Physics.gravity.y * Time.fixedDeltaTime : 0f;
        rb.linearVelocity = velocity;

        if (steppedUp)
            SteppedUp?.Invoke(distance);
    }

    private bool TryCapsuleCastAny(
        Vector3 pointA,
        Vector3 pointB,
        float radius,
        Vector3 castDirection,
        float distance,
        out RaycastHit nearest)
    {
        nearest = default;
        int hitCount = Physics.CapsuleCastNonAlloc(
            pointA,
            pointB,
            radius,
            castDirection,
            stepHits,
            distance,
            SolidLayers,
            QueryTriggerInteraction.Ignore);
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = stepHits[i];

            if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                continue;

            if (!found || hit.distance < nearest.distance)
            {
                nearest = hit;
                found = true;
            }
        }

        return found;
    }

    private bool TryRaycastStatic(Vector3 origin, float distance, out RaycastHit nearest)
    {
        nearest = default;
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            stepHits,
            distance,
            SolidLayers,
            QueryTriggerInteraction.Ignore);
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = stepHits[i];

            if (hit.collider == null ||
                hit.collider.transform.IsChildOf(transform) ||
                hit.collider.attachedRigidbody != null)
            {
                continue;
            }

            if (!found || hit.distance < nearest.distance)
            {
                nearest = hit;
                found = true;
            }
        }

        return found;
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

    // Crouching wins over running rather than combining with it. A crouched
    // sprint would be the quiet gait at the loud speed, which is the one
    // combination the whole arrangement exists to refuse - and it would be
    // reached by holding two keys nobody would think to stop holding.
    private float GetTargetSpeed()
    {
        if (movement == null)
            return isCrouching ? speed * 0.55f : speed;

        return speed * movement.TotalScaleFor(
            isCrouching,
            IsRunningForward(),
            stamina);
    }

    // Deliberately never shown. The player finds out how much is left from how
    // fast they are going and from what they can hear themselves doing, which
    // is the whole reason there is no bar: a number turns pacing into
    // arithmetic, and breath turns it into a feeling.
    public float StaminaNormalized => stamina;
    public bool IsWinded => isWinded;
    public bool IsRunningNow => IsRunningForward();

    // Reset when the tank climbs back over the tiredness threshold: the cost
    // belongs to the spell it was paid in, and the spell is over when the
    // player is no longer tired.
    public float StaminaSpentInBurst => staminaSpentInBurst;

    private void TickStamina(float deltaTime)
    {
        if (movement == null || deltaTime <= 0f)
            return;

        if (IsRunningForward())
        {
            float before = stamina;

            stamina = Mathf.Max(
                0f,
                stamina - deltaTime / movement.RunSeconds);

            staminaSpentInBurst += before - stamina;

            // Emptied. Running stays shut until enough has come back, or the
            // key becomes a stutter button worth tapping.
            if (stamina <= 0f)
                isWinded = true;

            return;
        }

        // Three things decide how fast it comes back: the base rate, what
        // the player is doing while it does, and where in the tank they are.
        // The last one is a curve somebody draws, so the shape of a recovery
        // is a decision rather than a consequence of division.
        float rate = movement.RecoveryRateFor(isCrouching, IsMovingOnFoot()) *
                     movement.RecoveryCurveAt(stamina);

        stamina = Mathf.Min(
            1f,
            stamina + deltaTime * rate / movement.RecoverySeconds);

        if (isWinded && stamina >= movement.RecoveredEnoughToRun)
            isWinded = false;

        // Back above the line the tiredness starts at, so the effort that put
        // them under it is over and its cost stops counting. Running again
        // before that adds to the same total rather than starting a new one -
        // a second sprint out of a hole you never climbed out of is not a
        // second effort, it is more of the first.
        if (stamina >= movement.TiredBelow)
            staminaSpentInBurst = 0f;
    }

    // Running is a thing you do towards something.
    //
    // Backwards at a sprint is how a player fights an enemy that is faster
    // than them: keep her in frame, keep the distance, never turn around. The
    // whole reason to run is supposed to be that you have decided to stop
    // looking. Sideways goes with it - the same trick works at ninety degrees
    // and reads even stranger.
    //
    // Judged on the input rather than on the velocity, because velocity lags
    // and a player who has just let go of forward would keep sprinting through
    // the turn. The dead zone is the one the rest of the movement already
    // uses: at rest the stick is never quite centred, and without it a run
    // would flicker on and off while somebody stands still leaning on shift.
    // Moving under your own feet, which is not the same as having a velocity:
    // being shoved by an item or sliding down something is not walking, and
    // resting through it should not be punished as though it were.
    private bool IsMovingOnFoot()
    {
        return direction.sqrMagnitude > moveInputDeadZone * moveInputDeadZone;
    }

    private bool IsRunningForward()
    {
        return isRunning && !isWinded && direction.y > moveInputDeadZone;
    }

    // Held, not toggled. Letting go is how you stop.
    private void UpdateIsRunning(bool value)
    {
        isRunning = value;
    }

    private bool CheckGrounded()
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
            SolidLayers,
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

        if (draggable != null)
            draggable.RequestPushAuthority();

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

    // Hold and toggle are the same two rules read in a different order.
    //
    // Toggle only has an opinion about the key going down: a press asks for the
    // other stance. Hold has one about both edges: down is crouched, up is
    // standing, and there is nothing to remember between them.
    //
    // Standing is the half that can be refused - there may be a ceiling - so it
    // goes through the same clearance check either way. A hold that is let go
    // under a table leaves the player crouched, which is the truth, and the
    // stance comes back the moment they walk out from under it.
    public void UpdateIsCrouching(bool isCrouchKeyDown)
    {
        if (movementBlockers.Count > 0)
            return;

        if (!NextCrouchState(crouchIsHold, isCrouchKeyDown, isCrouching, out bool nextIsCrouching))
            return;

        if (!nextIsCrouching && !CanStandUp())
        {
            // Only a hold keeps asking. A toggle that could not stand has been
            // answered - the player presses again when they have room.
            wantsToStand = crouchIsHold;
            return;
        }

        wantsToStand = false;
        SetCrouchState(nextIsCrouching, true);
    }

    // The decision on its own, with no body attached to it and nothing it can
    // refuse - whether the stance is possible is the caller's question, and this
    // one is only what the player asked for. Both modes read off the same two
    // facts, which is the whole reason it fits in one function.
    //
    // False means the key said nothing: a toggle hearing a release, or either
    // mode being asked for the stance it is already in.
    public static bool NextCrouchState(
        bool isHold,
        bool isKeyDown,
        bool isCrouching,
        out bool nextIsCrouching)
    {
        nextIsCrouching = isHold
            ? isKeyDown
            : isKeyDown && !isCrouching;

        if (!isHold && !isKeyDown)
            return false;

        return nextIsCrouching != isCrouching;
    }

    // Handed over when the local player's scope opens, the same way the camera
    // gets it. Only the local player's stance is decided here - everybody
    // else's arrives over the network already decided - so a remote copy of a
    // player never asks for this and is never given it.
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

    // Switching to hold while crouched leaves the stance where it is: the next
    // press is what decides, and standing somebody up because they opened a
    // settings screen is not what the setting says.
    private void ApplySettings()
    {
        if (settingsService != null)
            crouchIsHold = settingsService.Current.crouchIsHold;
    }

    // A hold let go under a table cannot stand up yet, and the key will not be
    // released a second time to ask again. So the wish is remembered and tried
    // once a tick until the ceiling is gone - which, from the player's side, is
    // standing up the moment they walk out from under it.
    private void UpdatePendingStand()
    {
        if (!wantsToStand)
            return;

        if (!crouchIsHold || !isCrouching)
        {
            wantsToStand = false;
            return;
        }

        if (movementBlockers.Count > 0 || !CanStandUp())
            return;

        wantsToStand = false;
        SetCrouchState(false, true);
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

    public void SetMovementActive(object source, bool value)
    {
        if (source == null)
        {
            movementBlockers.Clear();
        }
        else if (value)
        {
            movementBlockers.Remove(source);
        }
        else
        {
            movementBlockers.Add(source);
        }

        if (movementBlockers.Count > 0)
        {
            direction = Vector2.zero;
        }
    }

    public bool IsMovementActive => movementBlockers.Count == 0;

    // Cached ground state and last move input, read by PlayerCameraEffects each frame
    // instead of every effect reaching into this component or Rigidbody on its own.
    public bool IsGrounded { get; private set; }
    public Vector2 MoveInput => direction;
}
